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

        /// <summary>斷線後的重連間隔（秒）。</summary>
        public double ReconnectSeconds = 2.0;

        /// <summary>大螢幕推播賽況快照的頻率（每秒次數）。</summary>
        public double SnapshotsPerSecond = 10.0;

        public void Validate()
        {
            if (string.IsNullOrEmpty(RelayUrl))
            {
                RelayUrl = "ws://localhost:8080/ws?role=host";
                AutoConnect = false;
            }

            ReconnectSeconds = ConfigMath.Clamp(ReconnectSeconds, 0.5, 60.0);
            SnapshotsPerSecond = ConfigMath.Clamp(SnapshotsPerSecond, 1.0, 60.0);
        }
    }
}
