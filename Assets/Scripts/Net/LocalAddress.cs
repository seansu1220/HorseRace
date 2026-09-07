using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace HorseRace.Net
{
    /// <summary>
    /// 找出本機在區網上的 IPv4 位址，用來組出手機掃 QRCode 後要開的網址。
    /// </summary>
    public static class LocalAddress
    {
        private static string _cached;

        /// <summary>
        /// 回傳本機的區網 IPv4；找不到時回傳 null。
        ///
        /// 挑選條件是「有設定閘道」——開發機上常有 VMware、VirtualBox、WSL 之類的
        /// 虛擬網卡，它們同樣是 Up 狀態也有 IPv4，但沒有預設閘道。
        /// 手機連得到的一定是有閘道的那張實體網卡。
        /// </summary>
        public static string FindLanIPv4()
        {
            if (_cached != null)
            {
                return _cached.Length == 0 ? null : _cached;
            }

            _cached = string.Empty;

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up
                        || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    if (!HasUsableGateway(properties))
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            _cached = address.Address.ToString();
                            return _cached;
                        }
                    }
                }

                Debug.LogWarning("[LocalAddress] 找不到有閘道的區網位址，QRCode 可能無法使用。"
                                 + "請在設定檔的 Network.JoinUrl 直接填入手機要開的網址。");
            }
            catch (Exception error)
            {
                Debug.LogWarning("[LocalAddress] 查詢網路介面失敗："
                                 + error.GetType().Name + " - " + error.Message);
            }

            return null;
        }

        private static bool HasUsableGateway(IPInterfaceProperties properties)
        {
            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
            {
                if (gateway.Address == null
                    || gateway.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                // 虛擬網卡有時會掛一個 0.0.0.0 的假閘道
                if (!IPAddress.Any.Equals(gateway.Address))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 從中繼伺服器的 WebSocket 位址推導出手機要開的網頁位址。
        /// ws → http、wss → https，並丟掉路徑與查詢字串；
        /// 若主機是 localhost 就換成本機的區網 IP。
        /// </summary>
        public static string DeriveJoinUrl(string relayUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(relayUrl, UriKind.Absolute, out uri))
            {
                return null;
            }

            bool secure = uri.Scheme == "wss" || uri.Scheme == "https";
            string host = uri.Host;

            if (IsLoopback(host))
            {
                string lan = FindLanIPv4();
                if (string.IsNullOrEmpty(lan))
                {
                    return null;
                }

                host = lan;
            }

            string scheme = secure ? "https" : "http";
            bool defaultPort = (secure && uri.Port == 443) || (!secure && uri.Port == 80);

            return defaultPort
                ? scheme + "://" + host + "/"
                : scheme + "://" + host + ":" + uri.Port + "/";
        }

        private static bool IsLoopback(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || host == "127.0.0.1"
                   || host == "::1";
        }
    }
}
