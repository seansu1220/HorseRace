using System;
using System.IO;
using HorseRace.Core;

namespace HorseRace.Net
{
    /// <summary>
    /// <see cref="LocalRelayLauncher"/> 需要的所有參數，全部是純字串與數值。
    /// 路徑在主執行緒解析好再傳進來，啟動器本身不必碰 Unity API。
    /// </summary>
    public sealed class LocalRelayOptions
    {
        private const int DefaultWebSocketPort = 80;
        private const string ServerFolderName = "server";
        private const string ToolsFolderName = "tools";
        private const string CloudflaredFileName = "cloudflared.exe";
        private const string HostKeyFileName = "relay-host-key.txt";

        public int Port;
        public string ServerDirectory;
        public string NodeCommand;
        public bool UseTunnel;
        public string CloudflaredPath;
        public string CloudflaredInstallPath;
        public string CloudflaredDownloadUrl;
        public double TunnelTimeoutSeconds;
        public string HostKeyFile;

        /// <summary>
        /// 是否該由大螢幕自己開中繼伺服器：只有連線位址指向本機時才會。
        /// 指向雲端時伺服器在別人家，這裡什麼都不做。
        /// </summary>
        public static bool ShouldLaunch(NetworkConfig network)
        {
            if (network == null || !network.AutoConnect || !network.AutoStartLocalRelay)
            {
                return false;
            }

            Uri uri;
            return Uri.TryCreate(network.RelayUrl, UriKind.Absolute, out uri)
                   && LocalAddress.IsLoopbackHost(uri.Host);
        }

        /// <param name="dataPath">Unity 的 Application.dataPath（編輯器是 Assets/，建置版是 XXX_Data/）。</param>
        /// <param name="persistentDataPath">Unity 的 Application.persistentDataPath，放下載的工具與金鑰。</param>
        public static LocalRelayOptions Create(NetworkConfig network, string dataPath, string persistentDataPath)
        {
            Uri uri = new Uri(network.RelayUrl);

            // 編輯器：dataPath 的上一層是專案根目錄；建置版：是 exe 所在資料夾。兩者旁邊都放 server/
            string baseDirectory = Path.GetFullPath(Path.Combine(dataPath, ".."));

            return new LocalRelayOptions
            {
                Port = uri.Port > 0 ? uri.Port : DefaultWebSocketPort,
                ServerDirectory = ResolveServerDirectory(network.ServerDirectory, baseDirectory),
                NodeCommand = network.NodeCommand,
                UseTunnel = network.UseTunnel,
                CloudflaredPath = network.CloudflaredPath,
                CloudflaredInstallPath = Path.Combine(persistentDataPath, ToolsFolderName, CloudflaredFileName),
                CloudflaredDownloadUrl = network.CloudflaredDownloadUrl,
                TunnelTimeoutSeconds = network.TunnelTimeoutSeconds,
                HostKeyFile = Path.Combine(persistentDataPath, HostKeyFileName)
            };
        }

        private static string ResolveServerDirectory(string configured, string baseDirectory)
        {
            if (string.IsNullOrEmpty(configured))
            {
                return Path.Combine(baseDirectory, ServerFolderName);
            }

            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(baseDirectory, configured));
        }
    }
}
