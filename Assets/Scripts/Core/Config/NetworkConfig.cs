using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 大螢幕連往中繼伺服器的設定。放設定檔的理由：開發時指向 localhost、
    /// 活動時指向雲端，換一行 JSON 就好，不必重出版本。
    /// </summary>
    [Serializable]
    public sealed class NetworkConfig
    {
        /// <summary>中繼伺服器的 WebSocket 位址。正式環境請用 wss://。</summary>
        public string RelayUrl = "ws://localhost:8080/ws?role=host";

        /// <summary>是否在啟動時自動連線。關掉就是純單機模式，方便只測賽事本身。</summary>
        public bool AutoConnect = true;

        /// <summary>
        /// 手機掃 QRCode 之後要開的網址。
        ///
        /// 留空則自動從 <see cref="RelayUrl"/> 推導（ws → http、wss → https），
        /// 且若位址是 localhost 會自動換成本機的區網 IP——不然手機掃了會連到自己身上。
        /// 用 cloudflared 或部署到雲端時，把公開網址填在這裡。
        /// </summary>
        public string JoinUrl = "";

        /// <summary>斷線後的重連間隔（秒）。</summary>
        public double ReconnectSeconds = 2.0;

        /// <summary>大螢幕推播賽況快照的頻率（每秒次數）。</summary>
        public double SnapshotsPerSecond = 10.0;

        /// <summary>
        /// <see cref="RelayUrl"/> 指向本機時，由大螢幕自己把中繼伺服器（Node.js）開起來，
        /// 不必另外開終端機。該埠已經有伺服器在跑時會直接沿用。
        /// </summary>
        public bool AutoStartLocalRelay = true;

        /// <summary>
        /// 同時開一條 Cloudflare 臨時通道，讓手機用公開的 https 網址連進來：
        /// 不必和大螢幕在同一個 Wi-Fi，也才拿得到動作感測器權限。
        /// 通道建立失敗時 QRCode 自動退回區網網址。
        /// </summary>
        public bool UseTunnel = true;

        /// <summary>中繼伺服器程式所在資料夾。留空＝專案或建置版旁邊的 server/；相對路徑以該處的上一層為基準。</summary>
        public string ServerDirectory = "";

        /// <summary>Node.js 執行檔。預設從 PATH 找。</summary>
        public string NodeCommand = "node";

        /// <summary>cloudflared.exe 的位置。留空＝先找 PATH，找不到就自動下載到使用者資料夾。</summary>
        public string CloudflaredPath = "";

        /// <summary>cloudflared 的下載來源。</summary>
        public string CloudflaredDownloadUrl = DefaultCloudflaredDownloadUrl;

        /// <summary>等通道就緒的上限（秒），逾時就先用區網網址頂著，通道晚點好了會自動換上。</summary>
        public double TunnelTimeoutSeconds = 45.0;

        public const string DefaultNodeCommand = "node";

        public const string DefaultCloudflaredDownloadUrl =
            "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

        public void Validate()
        {
            if (string.IsNullOrEmpty(RelayUrl))
            {
                RelayUrl = "ws://localhost:8080/ws?role=host";
                AutoConnect = false;
            }

            ReconnectSeconds = ConfigMath.Clamp(ReconnectSeconds, 0.5, 60.0);
            SnapshotsPerSecond = ConfigMath.Clamp(SnapshotsPerSecond, 1.0, 60.0);

            if (ServerDirectory == null)
            {
                ServerDirectory = "";
            }

            if (CloudflaredPath == null)
            {
                CloudflaredPath = "";
            }

            if (string.IsNullOrEmpty(NodeCommand))
            {
                NodeCommand = DefaultNodeCommand;
            }

            if (string.IsNullOrEmpty(CloudflaredDownloadUrl))
            {
                CloudflaredDownloadUrl = DefaultCloudflaredDownloadUrl;
            }

            TunnelTimeoutSeconds = ConfigMath.Clamp(TunnelTimeoutSeconds, 5.0, 300.0);
        }
    }
}
