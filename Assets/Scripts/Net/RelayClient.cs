using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HorseRace.Net
{
    /// <summary>
    /// 大螢幕連往中繼伺服器的 WebSocket 客戶端。
    ///
    /// 用 .NET 內建的 <see cref="ClientWebSocket"/>，不引入任何第三方套件——
    /// 目標平台是 Windows Standalone，Mono 執行期原生就支援。
    /// （若未來要出 WebGL 版，這個類別要換成 jslib 實作，介面維持不變即可。）
    ///
    /// 執行緒約定：收送都在背景 Task 上跑，對外只透過兩個 thread-safe 佇列溝通。
    /// **呼叫端必須在主執行緒消化 <see cref="TryDequeue"/>**，
    /// 絕不可在背景執行緒碰 Unity API 或賽事狀態。
    /// </summary>
    public sealed class RelayClient : IDisposable
    {
        private const int ReceiveBufferSize = 8 * 1024;
        private const int SendPollMilliseconds = 10;

        private readonly ConcurrentQueue<string> _inbound = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outbound = new ConcurrentQueue<string>();

        private CancellationTokenSource _cancellation;
        private Task _worker;

        private volatile bool _connected;
        private volatile string _statusText = "尚未連線";

        /// <summary>目前是否連上。UI 直接顯示這個。</summary>
        public bool IsConnected
        {
            get { return _connected; }
        }

        /// <summary>給人看的連線狀態描述，現場出問題時第一個要看的東西。</summary>
        public string StatusText
        {
            get { return _statusText; }
        }

        /// <summary>開始連線，並在斷線後自動重試。重複呼叫會被忽略。</summary>
        public void Start(string url, double reconnectSeconds)
        {
            if (_worker != null)
            {
                return;
            }

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                _statusText = "連線位址格式錯誤：" + url;
                Debug.LogError("[RelayClient] " + _statusText);
                return;
            }

            int reconnectMilliseconds = Mathf.Max(500, Mathf.RoundToInt((float)reconnectSeconds * 1000f));
            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(uri, reconnectMilliseconds, _cancellation.Token));
        }

        /// <summary>取出一則收到的訊息。請在主執行緒的 Update 迴圈中反覆呼叫到取空為止。</summary>
        public bool TryDequeue(out string message)
        {
            return _inbound.TryDequeue(out message);
        }

        /// <summary>排入一則要送出的訊息。未連線時會被丟棄，不會累積成待爆的佇列。</summary>
        public void Send(string json)
        {
            if (!_connected || string.IsNullOrEmpty(json))
            {
                return;
            }

            _outbound.Enqueue(json);
        }

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
                Debug.LogWarning("[RelayClient] 取消連線時發生例外：" + error.Message);
            }

            // 不 Wait()：這通常在 OnDestroy 呼叫，卡住主執行緒會讓編輯器停不下來。
            // 背景工作看到取消旗標就會自己收工。
            _cancellation.Dispose();
            _cancellation = null;
            _worker = null;
            _connected = false;
            _statusText = "已關閉";
        }

        // ---- 背景工作 ----

        private async Task RunAsync(Uri uri, int reconnectMilliseconds, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                ClientWebSocket socket = null;

                try
                {
                    socket = new ClientWebSocket();
                    _statusText = "連線中…";

                    await socket.ConnectAsync(uri, token).ConfigureAwait(false);

                    _connected = true;
                    _statusText = "已連線";

                    Task receiving = ReceiveLoopAsync(socket, token);
                    Task sending = SendLoopAsync(socket, token);

                    // 任一邊結束就代表這條連線走完了，回到外層重連
                    await Task.WhenAny(receiving, sending).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception error)
                {
                    _statusText = "連線失敗：" + error.GetType().Name;
                }
                finally
                {
                    _connected = false;
                    if (socket != null)
                    {
                        socket.Dispose();
                    }
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                _statusText = "已斷線，重連中…";
                DrainOutbound();

                try
                {
                    await Task.Delay(reconnectMilliseconds, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _connected = false;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            StringBuilder pending = new StringBuilder();

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket
                    .ReceiveAsync(new ArraySegment<byte>(buffer), token)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                // 一則訊息可能被拆成多個框，收齊了才送進佇列
                if (result.EndOfMessage)
                {
                    _inbound.Enqueue(pending.ToString());
                    pending.Length = 0;
                }
            }
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string payload;
                if (!_outbound.TryDequeue(out payload))
                {
                    await Task.Delay(SendPollMilliseconds, token).ConfigureAwait(false);
                    continue;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                await socket
                    .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>斷線時把待送訊息清掉。這些都是即時狀態，補送過期資料只會造成畫面倒退。</summary>
        private void DrainOutbound()
        {
            string discarded;
            while (_outbound.TryDequeue(out discarded))
            {
                // 只是清空
            }
        }
    }
}
