using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HorseRace.Net
{
    /// <summary>外網通道的狀態。決定大螢幕的 QRCode 要放哪個網址。</summary>
    public enum TunnelState
    {
        /// <summary>設定關閉，只用區網網址。</summary>
        Disabled,

        /// <summary>程式剛啟動、通道還在準備（含第一次下載 cloudflared）。QRCode 先不顯示。</summary>
        Starting,

        /// <summary>通道可用，QRCode 放公開的 https 網址。</summary>
        Ready,

        /// <summary>通道失敗或中斷，QRCode 暫時退回區網網址；背景仍會持續重試。</summary>
        Unavailable
    }

    /// <summary>某一瞬間的啟動器狀態。struct 複本，主執行緒拿去讀不必上鎖。</summary>
    public struct LocalRelayStatus
    {
        public TunnelState Tunnel;

        /// <summary>只有 <see cref="TunnelState.Ready"/> 時才有值。</summary>
        public string PublicUrl;

        /// <summary>給人看的通道說明，例如「下載通道程式中… 42%」。</summary>
        public string TunnelDetail;

        /// <summary>中繼伺服器的問題；null 代表正常。</summary>
        public string ServerProblem;
    }

    /// <summary>啟動器要寫進 log 的一行。</summary>
    public struct LauncherLogLine
    {
        public bool IsWarning;
        public string Text;
    }

    /// <summary>
    /// 讓大螢幕自己把中繼伺服器（Node.js）與 Cloudflare 臨時通道開起來，
    /// 現場只要開大螢幕程式就好，不必另外開終端機打指令。
    ///
    /// 伺服器本身沒有任何改變——同一份 server/ 程式碼日後照樣可以部署到雲端，
    /// 那時把 RelayUrl 改成雲端位址，這個類別就不會啟動。
    ///
    /// 執行緒約定：所有工作都在背景 Task 上跑，本類別不碰任何 Unity API。
    /// 主執行緒透過 <see cref="Status"/> 讀狀態、透過 <see cref="TryDequeueLog"/> 取 log。
    /// </summary>
    public sealed class LocalRelayLauncher : IDisposable
    {
        private const string HostKeyQueryName = "key";
        private const string HostKeyEnvironmentName = "HOST_KEY";
        private const string PortEnvironmentName = "PORT";

        private static readonly TimeSpan PortProbeTimeout = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan NpmInstallTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ServerRestartDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ExternalServerPollInterval = TimeSpan.FromSeconds(5);

        /// <summary>Node.js 官方安裝程式的預設位置，PATH 沒設好時的備援。</summary>
        private static readonly string DefaultNodeInstallPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");

        private readonly LocalRelayOptions _options;
        private readonly ConcurrentQueue<LauncherLogLine> _logs = new ConcurrentQueue<LauncherLogLine>();
        private readonly object _statusGate = new object();
        private readonly List<ChildProcess> _children = new List<ChildProcess>();

        private LocalRelayStatus _status;
        private bool _leftInitialStart;
        private CancellationTokenSource _cancellation;

        /// <summary>由 <see cref="_children"/> 的鎖保護。</summary>
        private bool _disposed;

        public LocalRelayLauncher(LocalRelayOptions options)
        {
            _options = options;
            HostKey = HostKeyStore.LoadOrCreate(options.HostKeyFile, Warn);

            _status.Tunnel = options.UseTunnel ? TunnelState.Starting : TunnelState.Disabled;
            _status.TunnelDetail = options.UseTunnel ? "外網通道準備中…" : "";
        }

        /// <summary>大螢幕連線時要帶的金鑰，伺服器由本類別啟動時會設定成同一把。</summary>
        public string HostKey { get; private set; }

        public LocalRelayStatus Status
        {
            get
            {
                lock (_statusGate)
                {
                    return _status;
                }
            }
        }

        /// <summary>在連線位址後面加上大螢幕金鑰。</summary>
        public static string WithHostKey(string relayUrl, string hostKey)
        {
            if (string.IsNullOrEmpty(hostKey))
            {
                return relayUrl;
            }

            string separator = relayUrl.IndexOf('?') >= 0 ? "&" : "?";
            return relayUrl + separator + HostKeyQueryName + "=" + Uri.EscapeDataString(hostKey);
        }

        /// <summary>開始在背景啟動伺服器與通道。重複呼叫會被忽略。</summary>
        public void Start()
        {
            if (_cancellation != null)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            Task.Run(() => RunServerAsync(token));

            if (_options.UseTunnel)
            {
                Task.Run(() => RunTunnelAsync(token));
                Task.Run(() => FallBackIfTunnelSlowAsync(token));
            }
        }

        /// <summary>取出一行 log。請在主執行緒反覆呼叫到取空為止。</summary>
        public bool TryDequeueLog(out LauncherLogLine line)
        {
            return _logs.TryDequeue(out line);
        }

        /// <summary>停止所有背景工作，並同步收掉由本類別啟動的子行程。</summary>
        public void Dispose()
        {
            if (_cancellation == null)
            {
                return;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (Exception error)
            {
                Warn("[LocalRelay] 取消背景工作時發生例外：" + error.Message);
            }

            // 背景工作收到取消後也會自己收，但那是非同步的；程式正要結束時等不到，這裡直接砍
            ChildProcess[] running;
            lock (_children)
            {
                _disposed = true;
                running = _children.ToArray();
                _children.Clear();
            }

            foreach (ChildProcess child in running)
            {
                child.Dispose();
            }

            _cancellation.Dispose();
            _cancellation = null;
        }

        // ---- 中繼伺服器 ----

        private async Task RunServerAsync(CancellationToken token)
        {
            try
            {
                await WaitWhileExternalServerRunsAsync(token).ConfigureAwait(false);

                string entry = Path.Combine(_options.ServerDirectory, "src", "server.js");
                if (!File.Exists(entry))
                {
                    ReportServerProblem("找不到中繼伺服器程式", "找不到 " + entry
                        + "。建置版請確認 exe 旁邊有 server 資料夾，或在設定檔的 Network.ServerDirectory 指定位置。");
                    return;
                }

                string node = ExecutableLocator.Find(_options.NodeCommand, DefaultNodeInstallPath);
                if (node == null)
                {
                    ReportServerProblem("找不到 Node.js", "找不到 Node.js（" + _options.NodeCommand
                        + "）。請安裝 Node.js 18 以上版本，或在設定檔的 Network.NodeCommand 填入 node.exe 的完整路徑。");
                    return;
                }

                if (!await EnsureDependenciesAsync(node, token).ConfigureAwait(false))
                {
                    return;
                }

                await SuperviseServerAsync(node, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 程式結束
            }
            catch (Exception error)
            {
                ReportServerProblem("中繼伺服器啟動失敗",
                    "中繼伺服器啟動失敗：" + error.GetType().Name + " - " + error.Message);
            }
        }

        /// <summary>
        /// 該埠已經有伺服器（例如有人手動 npm start）時直接沿用，不重複開；
        /// 但持續盯著它，它一關掉就由本程式接手，現場不會因此斷線太久。
        /// </summary>
        private async Task WaitWhileExternalServerRunsAsync(CancellationToken token)
        {
            bool announced = false;

            while (await IsPortOpenAsync(_options.Port).ConfigureAwait(false))
            {
                if (!announced)
                {
                    announced = true;
                    Log("[LocalRelay] 埠 " + _options.Port + " 已有伺服器在執行，直接沿用（本程式不會關閉它）。");
                }

                await Task.Delay(ExternalServerPollInterval, token).ConfigureAwait(false);
            }

            if (announced)
            {
                Warn("[LocalRelay] 原本沿用的伺服器已關閉，改由本程式啟動。");
            }
        }

        /// <summary>啟動伺服器並守著它；意外結束就重開。</summary>
        private async Task SuperviseServerAsync(string node, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ChildProcess server;
                try
                {
                    server = StartChild(BuildServerStartInfo(node), OnServerOutput);
                }
                catch (Exception error)
                {
                    ReportServerProblem("中繼伺服器無法啟動",
                        "無法啟動中繼伺服器（" + node + "）：" + error.GetType().Name + " - " + error.Message);
                    return;
                }

                SetServerProblem(null);
                Log("[LocalRelay] 已啟動本機中繼伺服器（" + node + "，埠 " + _options.Port + "）");

                int exitCode = await WaitForExitAsync(server, token).ConfigureAwait(false);
                ReleaseChild(server);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                ReportServerProblem("中繼伺服器意外結束，重啟中…", "中繼伺服器意外結束（代碼 " + exitCode + "），"
                    + (int)ServerRestartDelay.TotalSeconds + " 秒後重新啟動。");
                await Task.Delay(ServerRestartDelay, token).ConfigureAwait(false);
            }
        }

        private ProcessStartInfo BuildServerStartInfo(string node)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = node,
                Arguments = Quote(Path.Combine("src", "server.js")),
                WorkingDirectory = _options.ServerDirectory
            };

            startInfo.Environment[PortEnvironmentName] = _options.Port.ToString();
            startInfo.Environment[HostKeyEnvironmentName] = HostKey;
            return startInfo;
        }

        private void OnServerOutput(string line)
        {
            bool looksLikeError = line.IndexOf("Error", StringComparison.Ordinal) >= 0
                                  || line.IndexOf("錯誤", StringComparison.Ordinal) >= 0;
            if (looksLikeError)
            {
                Warn(line);
            }
            else
            {
                Log(line);
            }
        }

        /// <summary>
        /// 第一次執行（例如另一台電腦剛 clone 下來）時 node_modules 不存在，自動 npm install。
        /// 建置版會把 node_modules 一起複製過去，正常不會走到這裡。
        /// </summary>
        private async Task<bool> EnsureDependenciesAsync(string node, CancellationToken token)
        {
            string marker = Path.Combine(_options.ServerDirectory, "node_modules", "ws", "package.json");
            if (File.Exists(marker))
            {
                return true;
            }

            Log("[LocalRelay] 第一次執行，正在安裝中繼伺服器的相依套件（npm install）…");
            SetServerProblem("安裝伺服器套件中…");

            ChildProcess install = StartChild(BuildNpmInstallStartInfo(node), Log);
            Task timeout = Task.Delay(NpmInstallTimeout, token);
            Task first = await Task.WhenAny(install.Exited, timeout).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            int exitCode = first == install.Exited ? install.Exited.Result : -1;
            install.Dispose();
            ReleaseChild(install);

            if (first != install.Exited)
            {
                ReportServerProblem("伺服器套件安裝逾時", "npm install 超過 "
                    + (int)NpmInstallTimeout.TotalMinutes + " 分鐘仍未完成，請檢查網路，或在 server 資料夾手動執行 npm install。");
                return false;
            }

            if (exitCode != 0 || !File.Exists(marker))
            {
                ReportServerProblem("伺服器套件安裝失敗", "npm install 失敗（代碼 " + exitCode
                    + "），請在 " + _options.ServerDirectory + " 手動執行 npm install。");
                return false;
            }

            Log("[LocalRelay] 相依套件安裝完成。");
            return true;
        }

        private ProcessStartInfo BuildNpmInstallStartInfo(string node)
        {
            const string InstallArguments = "install --omit=dev --no-audit --no-fund";

            // 直接用 node 跑 npm 本體，少一層 cmd.exe；找不到才退回 cmd /c npm
            string nodeDirectory = Path.GetDirectoryName(node) ?? "";
            string npmCli = Path.Combine(nodeDirectory, "node_modules", "npm", "bin", "npm-cli.js");

            ProcessStartInfo startInfo = File.Exists(npmCli)
                ? new ProcessStartInfo { FileName = node, Arguments = Quote(npmCli) + " " + InstallArguments }
                : new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/d /c npm " + InstallArguments };

            startInfo.WorkingDirectory = _options.ServerDirectory;
            return startInfo;
        }

        private static async Task<bool> IsPortOpenAsync(int port)
        {
            using (TcpClient client = new TcpClient())
            {
                Task connect = client.ConnectAsync(IPAddress.Loopback, port);

                // 逾時後 connect 可能稍晚才失敗，先掛上觀察者，免得變成未觀察的例外
                _ = connect.ContinueWith(
                    task => { AggregateException ignored = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);

                Task first = await Task.WhenAny(connect, Task.Delay(PortProbeTimeout)).ConfigureAwait(false);
                return first == connect && connect.Status == TaskStatus.RanToCompletion && client.Connected;
            }
        }

        // ---- 外網通道 ----

        private async Task RunTunnelAsync(CancellationToken token)
        {
            try
            {
                string executable = await EnsureCloudflaredAsync(token).ConfigureAwait(false);
                if (executable == null)
                {
                    return;
                }

                QuickTunnel tunnel = new QuickTunnel(executable, _options.Port, StartChild, Log, Warn, ReportTunnel);
                await tunnel.RunAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 程式結束
            }
            catch (Exception error)
            {
                ReportTunnel(TunnelState.Unavailable, null, "外網通道發生錯誤");
                Warn("[LocalRelay] 外網通道發生錯誤，QRCode 改用區網網址：" + error.GetType().Name + " - " + error.Message);
            }
        }

        private async Task<string> EnsureCloudflaredAsync(CancellationToken token)
        {
            string existing = CloudflaredInstaller.FindExisting(_options.CloudflaredPath, _options.CloudflaredInstallPath);
            if (existing != null)
            {
                return existing;
            }

            if (!string.IsNullOrEmpty(_options.CloudflaredPath))
            {
                ReportTunnel(TunnelState.Unavailable, null, "找不到指定的 cloudflared");
                Warn("[LocalRelay] 設定檔指定的 cloudflared 不存在：" + _options.CloudflaredPath + "，QRCode 改用區網網址。");
                return null;
            }

            Log("[LocalRelay] 第一次使用外網通道，正在下載 cloudflared（約 55 MB，只需下載一次；"
                + "網路慢時可能要好幾分鐘，期間 QRCode 先用區網網址）…");
            ReportTunnel(TunnelState.Starting, null, "下載通道程式中…");

            try
            {
                await CloudflaredInstaller.DownloadAsync(
                    _options.CloudflaredDownloadUrl, _options.CloudflaredInstallPath,
                    percent => ReportTunnel(TunnelState.Starting, null, "下載通道程式中… " + percent + "%"),
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                ReportTunnel(TunnelState.Unavailable, null, "通道程式下載失敗");
                Warn("[LocalRelay] 下載 cloudflared 失敗：" + error.GetType().Name + " - " + error.Message
                     + "。QRCode 改用區網網址。也可以手動下載後，在設定檔的 Network.CloudflaredPath 指定位置。");
                return null;
            }

            Log("[LocalRelay] cloudflared 下載完成：" + _options.CloudflaredInstallPath);
            return _options.CloudflaredInstallPath;
        }

        /// <summary>
        /// 通道遲遲沒好（第一次下載、網路慢）時，先讓 QRCode 退回區網網址，
        /// 不要讓大螢幕一直空著。通道之後好了會自動換上。
        /// </summary>
        private async Task FallBackIfTunnelSlowAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.TunnelTimeoutSeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool stillStarting;
            lock (_statusGate)
            {
                stillStarting = _status.Tunnel == TunnelState.Starting;
            }

            if (stillStarting)
            {
                ReportTunnel(TunnelState.Unavailable, null, "外網通道尚未就緒");
                Warn("[LocalRelay] 外網通道 " + (int)_options.TunnelTimeoutSeconds
                     + " 秒內未就緒，QRCode 暫用區網網址（通道好了會自動換上）。");
            }
        }

        /// <summary>
        /// 更新通道狀態。規則：只有程式剛啟動時可以處於 Starting（QRCode 空白）；
        /// 一旦離開過，之後的「重新建立中」一律當 Unavailable，QRCode 保持區網網址而不是變空白。
        /// </summary>
        private void ReportTunnel(TunnelState state, string publicUrl, string detail)
        {
            lock (_statusGate)
            {
                if (state == TunnelState.Starting && _leftInitialStart)
                {
                    state = TunnelState.Unavailable;
                }

                if (state != TunnelState.Starting)
                {
                    _leftInitialStart = true;
                }

                _status.Tunnel = state;
                _status.PublicUrl = state == TunnelState.Ready ? publicUrl : null;
                _status.TunnelDetail = detail;
            }
        }

        // ---- 子行程與 log ----

        private ChildProcess StartChild(ProcessStartInfo startInfo, Action<string> onOutputLine)
        {
            ChildProcess child = ChildProcess.Start(startInfo, onOutputLine, Warn);

            bool disposed;
            lock (_children)
            {
                disposed = _disposed;
                if (!disposed)
                {
                    _children.Add(child);
                }
            }

            // 啟動的瞬間剛好遇上關閉：這個行程已經沒人會收，當場收掉
            if (disposed)
            {
                child.Dispose();
                throw new OperationCanceledException("啟動器已關閉");
            }

            return child;
        }

        private void ReleaseChild(ChildProcess child)
        {
            lock (_children)
            {
                _children.Remove(child);
            }

            child.Dispose();
        }

        private static async Task<int> WaitForExitAsync(ChildProcess child, CancellationToken token)
        {
            Task cancelled = Task.Delay(Timeout.Infinite, token);
            Task first = await Task.WhenAny(child.Exited, cancelled).ConfigureAwait(false);
            return first == child.Exited ? child.Exited.Result : -1;
        }

        private void ReportServerProblem(string shortText, string detail)
        {
            SetServerProblem(shortText);
            Warn("[LocalRelay] " + detail);
        }

        private void SetServerProblem(string problem)
        {
            lock (_statusGate)
            {
                _status.ServerProblem = problem;
            }
        }

        private void Log(string text)
        {
            _logs.Enqueue(new LauncherLogLine { IsWarning = false, Text = text });
        }

        private void Warn(string text)
        {
            _logs.Enqueue(new LauncherLogLine { IsWarning = true, Text = text });
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }
    }
}
