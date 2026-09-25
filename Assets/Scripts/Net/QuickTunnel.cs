using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HorseRace.Net
{
    /// <summary>
    /// 用 cloudflared 開一條 Cloudflare 臨時通道（Quick Tunnel），把本機中繼伺服器
    /// 以 https://xxxx.trycloudflare.com 的公開網址開放給手機。
    ///
    /// 免帳號、免費，代價是網址每次啟動都不同，所以要從 cloudflared 的輸出裡把網址抓出來。
    /// cloudflared 意外結束時會自動重開（網址會換，QRCode 由呼叫端跟著換）。
    /// </summary>
    internal sealed class QuickTunnel : IDisposable
    {
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        // DNS 生效的時間點（實測）：網址印出後約 3.5～7 秒公共 DNS 才查得到。
        // 太早查會讓解析器快取「查無此網域」最多 60 秒（trycloudflare.com 的 SOA 負快取時間），
        // 同一個 Wi-Fi 的手機就會跟著打不開，所以先等一下才開始查，查的間隔也不要太密。
        private static readonly TimeSpan DnsFirstCheckDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DnsCheckInterval = TimeSpan.FromSeconds(2);

        /// <summary>DoH 一直查不到（例如網路擋掉 DoH）時，超過這個時間就當作已生效——實測遠超過所需時間。</summary>
        private static readonly TimeSpan DnsAssumeReadyAfter = TimeSpan.FromSeconds(20);

        /// <summary>
        /// 用公共 DNS 的 DoH 介面確認網址已生效。刻意不用本機的 DNS 查：
        /// 本機解析器通常就是現場手機用的那台，太早查會把「查無此網域」快取給所有人。
        /// </summary>
        private static readonly string[] DnsOverHttpsEndpoints =
        {
            "https://1.1.1.1/dns-query?name={0}&type=A",
            "https://dns.google/resolve?name={0}&type=A"
        };

        /// <summary>
        /// 公開網址的格式。排除 api. 開頭：cloudflared 申請失敗時的錯誤訊息裡
        /// 會出現 https://api.trycloudflare.com，那不是我們的網址。
        /// </summary>
        private const string PublicUrlRegex = @"https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com";

        /// <summary>
        /// 用到時才建立。本類別刻意不放任何「可能失敗」的靜態初始化（見 <see cref="_dnsClient"/> 的說明）；
        /// 兩條執行緒同時建立也只是多一個相同的物件，無害。
        /// </summary>
        private static Regex _publicUrlPattern;

        private static Regex PublicUrlPattern
        {
            get
            {
                Regex pattern = _publicUrlPattern;
                if (pattern == null)
                {
                    pattern = new Regex(PublicUrlRegex, RegexOptions.IgnoreCase);
                    _publicUrlPattern = pattern;
                }

                return pattern;
            }
        }

        private const string RegisteredMarker = "Registered tunnel connection";
        private const string ErrorMarker = " ERR ";

        private static readonly TimeSpan DnsQueryTimeout = TimeSpan.FromSeconds(5);

        private readonly string _executablePath;
        private readonly int _localPort;
        private readonly Func<ProcessStartInfo, Action<string>, ChildProcess> _startChild;
        private readonly Action<string> _log;
        private readonly Action<string> _warn;
        private readonly Action<TunnelState, string, string> _report;

        private readonly Func<HttpClient> _dnsClientFactory;
        private readonly object _dnsClientGate = new object();

        /// <summary>
        /// 查 DNS 用的連線，用到時才建立。刻意不做成靜態欄位：
        /// Unity 剛進 Play 時多條背景執行緒同時初始化網路元件，建立 HttpClient 偶爾會失敗，
        /// 靜態初始化只要失敗一次，整個類別在這次 Play 中就永久無法使用（TypeInitializationException）。
        /// </summary>
        private HttpClient _dnsClient;
        private bool _dnsClientUnavailable;

        private volatile string _lastError;

        /// <param name="startChild">啟動子行程的方式，由擁有者提供，以便它在關閉時能同步收掉。</param>
        /// <param name="report">回報（狀態、公開網址、給人看的說明）。會在背景執行緒被呼叫。</param>
        /// <param name="dnsClientFactory">建立查 DNS 用的 HttpClient；null 用預設。測試時可注入會失敗的版本。</param>
        public QuickTunnel(
            string executablePath, int localPort,
            Func<ProcessStartInfo, Action<string>, ChildProcess> startChild,
            Action<string> log, Action<string> warn, Action<TunnelState, string, string> report,
            Func<HttpClient> dnsClientFactory = null)
        {
            _executablePath = executablePath;
            _localPort = localPort;
            _startChild = startChild;
            _log = log;
            _warn = warn;
            _report = report;
            _dnsClientFactory = dnsClientFactory ?? CreateDefaultDnsClient;
        }

        public void Dispose()
        {
            lock (_dnsClientGate)
            {
                if (_dnsClient != null)
                {
                    _dnsClient.Dispose();
                    _dnsClient = null;
                }
            }
        }

        private static HttpClient CreateDefaultDnsClient()
        {
            return new HttpClient { Timeout = DnsQueryTimeout };
        }

        /// <summary>
        /// 取得查 DNS 用的連線；建立失敗就記一次警告並回傳 null，
        /// 之後改走「等一段時間就視為生效」的備援，通道照樣能用。
        /// </summary>
        private HttpClient GetDnsClient()
        {
            lock (_dnsClientGate)
            {
                if (_dnsClient != null || _dnsClientUnavailable)
                {
                    return _dnsClient;
                }

                try
                {
                    _dnsClient = _dnsClientFactory();
                }
                catch (Exception error)
                {
                    _dnsClientUnavailable = true;
                    _warn("[QuickTunnel] 無法建立查詢 DNS 的連線，改為等待 " + (int)DnsAssumeReadyAfter.TotalSeconds
                          + " 秒後視為生效：" + ErrorText.Describe(error));
                }

                return _dnsClient;
            }
        }

        /// <summary>持續維持通道，直到 <paramref name="token"/> 被取消。</summary>
        public async Task RunAsync(CancellationToken token)
        {
            TimeSpan retryDelay = InitialRetryDelay;

            while (!token.IsCancellationRequested)
            {
                _report(TunnelState.Starting, null, "外網通道建立中…");
                bool becameReady = await RunOnceAsync(token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (becameReady)
                {
                    retryDelay = InitialRetryDelay;
                }

                _report(TunnelState.Unavailable, null, "外網通道中斷，重新建立中…");
                string lastError = _lastError;
                _warn("[QuickTunnel] cloudflared 已結束，" + (int)retryDelay.TotalSeconds + " 秒後重新建立通道"
                      + (string.IsNullOrEmpty(lastError) ? "" : "。最後的錯誤：" + lastError));

                try
                {
                    await Task.Delay(retryDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // 連不上 Cloudflare 或被限流時逐步拉長間隔，避免狂敲對方的 API
                double doubled = Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds);
                retryDelay = TimeSpan.FromSeconds(doubled);
            }
        }

        /// <summary>從 cloudflared 的一行輸出中找出公開網址（含結尾斜線）。</summary>
        public static bool TryParsePublicUrl(string line, out string url)
        {
            Match match = PublicUrlPattern.Match(line ?? "");
            url = match.Success ? match.Value.ToLowerInvariant() + "/" : null;
            return match.Success;
        }

        /// <summary>這一行是否表示通道已連上 Cloudflare 的節點。</summary>
        public static bool IsConnectionRegistered(string line)
        {
            return line != null && line.IndexOf(RegisteredMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- 單次執行 ----

        /// <summary>跑一次 cloudflared，直到它結束或被取消。回傳這次是否曾經就緒。</summary>
        private async Task<bool> RunOnceAsync(CancellationToken token)
        {
            TaskCompletionSource<string> urlFound =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> registered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ChildProcess child;
            try
            {
                _lastError = null;
                child = _startChild(BuildStartInfo(), line => HandleLine(line, urlFound, registered));
            }
            catch (Exception error)
            {
                _lastError = ErrorText.Describe(error);
                return false;
            }

            using (child)
            using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task<bool> ready = WaitUntilReadyAsync(urlFound.Task, registered.Task, attempt.Token);
                Task first = await Task.WhenAny(ready, child.Exited).ConfigureAwait(false);

                bool becameReady = first == ready
                                   && ready.Status == TaskStatus.RanToCompletion
                                   && ready.Result;

                if (becameReady)
                {
                    string url = urlFound.Task.Result;
                    _log("[QuickTunnel] 外網通道已就緒：" + url);
                    _report(TunnelState.Ready, url, "外網通道已就緒");
                }

                // 停掉可能還在跑的 DNS 查詢，然後守著行程直到它結束或整體被取消
                attempt.Cancel();
                await Task.WhenAny(child.Exited, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
                return becameReady;
            }
        }

        private ProcessStartInfo BuildStartInfo()
        {
            // 不自動更新：現場跑到一半換版本重啟，網址就會變
            return new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = "tunnel --no-autoupdate --url http://127.0.0.1:" + _localPort
            };
        }

        /// <summary>
        /// 拿到網址之後，等「cloudflared 回報已連上節點」且「公共 DNS 查得到這個網址」都成立。
        /// 兩個都要：網址印出來時通道往往還沒接通；接通了（約 0.5 秒後）DNS 又要再幾秒才生效。
        /// 太早放出 QRCode，手機掃了只會看到錯誤頁或「找不到伺服器」。
        /// </summary>
        private async Task<bool> WaitUntilReadyAsync(
            Task<string> urlTask, Task<bool> registeredTask, CancellationToken token)
        {
            Task cancelled = Task.Delay(Timeout.Infinite, token);

            if (await Task.WhenAny(urlTask, cancelled).ConfigureAwait(false) != urlTask)
            {
                return false;
            }

            Task<bool> dnsReady = WaitForDnsAsync(new Uri(urlTask.Result).Host, token);
            Task ready = Task.WhenAll(registeredTask, dnsReady);

            Task first = await Task.WhenAny(ready, cancelled).ConfigureAwait(false);
            return first == ready && ready.Status == TaskStatus.RanToCompletion;
        }

        private async Task<bool> WaitForDnsAsync(string host, CancellationToken token)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            await Task.Delay(DnsFirstCheckDelay, token).ConfigureAwait(false);

            while (true)
            {
                if (await IsPublishedAsync(host, token).ConfigureAwait(false))
                {
                    _log("[QuickTunnel] DNS 已生效（" + elapsed.Elapsed.TotalSeconds.ToString("F1") + " 秒）");
                    return true;
                }

                if (elapsed.Elapsed >= DnsAssumeReadyAfter)
                {
                    _warn("[QuickTunnel] " + (int)DnsAssumeReadyAfter.TotalSeconds
                          + " 秒內無法透過公共 DNS 確認網址（網路可能擋了 DoH），視為已生效。");
                    return true;
                }

                await Task.Delay(DnsCheckInterval, token).ConfigureAwait(false);
            }
        }

        /// <summary>任一個公共 DNS 查得到 A 紀錄就算生效。查詢失敗一律當作還沒生效。</summary>
        private async Task<bool> IsPublishedAsync(string host, CancellationToken token)
        {
            HttpClient client = GetDnsClient();
            if (client == null)
            {
                return false;
            }

            Task<bool>[] queries = new Task<bool>[DnsOverHttpsEndpoints.Length];
            for (int i = 0; i < queries.Length; i++)
            {
                queries[i] = QueryDnsOverHttpsAsync(client, string.Format(DnsOverHttpsEndpoints[i], host), token);
            }

            bool[] answers = await Task.WhenAll(queries).ConfigureAwait(false);
            return Array.IndexOf(answers, true) >= 0;
        }

        private static async Task<bool> QueryDnsOverHttpsAsync(HttpClient client, string url, CancellationToken token)
        {
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");
                    using (HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }

                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return IsPositiveDnsAnswer(body);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// DoH JSON 回應是否為「查得到」：Status 0（NOERROR）且帶 Answer。
        /// 只看這兩個欄位，用字串判斷就好，不必為此引入 JSON 函式庫。
        /// </summary>
        public static bool IsPositiveDnsAnswer(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            string compact = body.Replace(" ", "");
            return compact.Contains("\"Status\":0,") && compact.Contains("\"Answer\":[{");
        }

        /// <summary>
        /// 處理 cloudflared 的一行輸出（背景執行緒）。
        /// 它啟動時會印幾十行資訊，只轉出真正有用的幾行，免得把大螢幕的 log 洗掉。
        /// </summary>
        private void HandleLine(
            string line, TaskCompletionSource<string> urlFound, TaskCompletionSource<bool> registered)
        {
            string url;
            if (TryParsePublicUrl(line, out url))
            {
                if (urlFound.TrySetResult(url))
                {
                    _log("[QuickTunnel] 取得公開網址：" + url);
                }

                return;
            }

            if (IsConnectionRegistered(line))
            {
                if (registered.TrySetResult(true))
                {
                    _log("[QuickTunnel] 已連上 Cloudflare 節點");
                }

                return;
            }

            if (line.IndexOf(ErrorMarker, StringComparison.Ordinal) >= 0)
            {
                _lastError = line;
                _warn("[cloudflared] " + line);
            }
        }
    }
}
