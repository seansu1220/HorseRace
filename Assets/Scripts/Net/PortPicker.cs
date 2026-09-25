using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HorseRace.Net
{
    /// <summary>
    /// 在啟動本機中繼伺服器之前決定要用哪個埠。
    ///
    /// 只用「連得上嗎」判斷不夠：實測曾發生連不上（看起來空著），node 卻 listen 失敗（EADDRINUSE），
    /// 結果伺服器一直重啟了一分多鐘。這裡改用和 node 相同的方式實際綁一次埠（所有位址、IPv4／IPv6 雙堆疊），
    /// 綁不上而且也不是我們能沿用的伺服器，就換下一個空的埠，不再乾等。
    /// </summary>
    internal static class PortPicker
    {
        /// <summary>預設埠被佔用時，往後找幾個。</summary>
        private const int SearchRange = 20;

        private const int ConnectProbeMilliseconds = 300;

        /// <summary>
        /// 回傳要使用的埠：預設埠可用或已有伺服器可沿用時就用預設埠，否則往後找第一個綁得上的。
        /// </summary>
        public static int Choose(int preferred, Action<string> log, Action<string> warn)
        {
            if (CanBind(preferred))
            {
                return preferred;
            }

            if (IsListening(preferred))
            {
                // 已經有伺服器在聽（例如有人手動 npm start），交給啟動器沿用
                return preferred;
            }

            string holder = PortOwner.Describe(preferred);
            for (int port = preferred + 1; port <= preferred + SearchRange; port++)
            {
                if (CanBind(port))
                {
                    warn("[LocalRelay] 埠 " + preferred + " 被佔用（" + holder + "），改用埠 " + port
                         + "。QRCode、外網通道與大螢幕連線都會自動跟著換。");
                    return port;
                }
            }

            warn("[LocalRelay] 埠 " + preferred + " 被佔用（" + holder + "），往後 " + SearchRange
                 + " 個埠也都無法使用，仍嘗試使用 " + preferred + "。");
            return preferred;
        }

        /// <summary>和 node 的 listen(port) 一樣綁在所有位址（IPv6 雙堆疊，含 IPv4）。</summary>
        public static bool CanBind(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.IPv6Any, port);
                listener.Server.DualMode = true;
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                // 沒有 IPv6 的環境退回只檢查 IPv4
                return CanBindIPv4(port);
            }
            finally
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
        }

        private static bool CanBindIPv4(int port)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool IsListening(int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    Task connect = client.ConnectAsync(IPAddress.Loopback, port);
                    return connect.Wait(ConnectProbeMilliseconds) && client.Connected;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>查出是誰佔著某個埠，只在出問題時才呼叫，讓 log 直接說出「是哪個程式」。</summary>
    internal static class PortOwner
    {
        private const int NetstatTimeoutMilliseconds = 3000;

        /// <summary>例：「PID 1234（node）LISTENING」；查不到時說明原因。</summary>
        public static string Describe(int port)
        {
            try
            {
                // -p TCP 只列 IPv4，IPv6 的佔用要另外查
                string table = RunNetstat("TCP") + RunNetstat("TCPv6");
                string suffix = ":" + port;
                StringBuilder found = new StringBuilder();

                foreach (string rawLine in table.Split('\n'))
                {
                    string[] columns = rawLine.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    // TCP  本機位址  外部位址  狀態  PID
                    if (columns.Length < 5 || columns[0] != "TCP" || !columns[1].EndsWith(suffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (found.Length > 0)
                    {
                        found.Append("；");
                    }

                    found.Append(columns[3]).Append(" PID ").Append(columns[4]).Append(ProcessNameOf(columns[4]));
                }

                // netstat 只列出正在聽或已連線的 socket；「綁了但沒在聽」或正在關閉中的舊程式看不到
                return found.Length > 0
                    ? found.ToString()
                    : "netstat 看不到佔用者，可能是綁了埠但沒在接連線的程式，或正在關閉中的舊程式";
            }
            catch (Exception error)
            {
                return "無法查詢佔用者：" + ErrorText.Describe(error);
            }
        }

        /// <summary>執行 netstat 列出指定協定的連線；逾時或失敗回傳空字串。</summary>
        private static string RunNetstat(string protocol)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p " + protocol,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using (Process process = Process.Start(startInfo))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(NetstatTimeoutMilliseconds))
                {
                    process.Kill();
                    return "";
                }

                return output.Result;
            }
        }

        private static string ProcessNameOf(string pidText)
        {
            int pid;
            if (!int.TryParse(pidText, out pid) || pid <= 0)
            {
                return "";
            }

            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return "（" + process.ProcessName + "）";
                }
            }
            catch (Exception)
            {
                return "（已結束）";
            }
        }
    }
}
