using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 一匹馬的能力值。三項屬性刻意保持互不重疊，讓玩家看得懂、也讓現場調參數有意義。
    /// </summary>
    [Serializable]
    public sealed class HorseConfig
    {
        /// <summary>顯示名稱。</summary>
        public string Name = "無名馬";

        /// <summary>代表色，格式 "#RRGGBB"。大螢幕與手機端共用同一組色碼。</summary>
        public string ColorHex = "#FFFFFF";

        /// <summary>基礎速度（公尺／秒）。決定整體強弱，也是賠率的主要來源。</summary>
        public double BaseSpeed = 17.0;

        /// <summary>耐力 0~1。越高則賽程後段掉速越少。</summary>
        public double Stamina = 0.5;

        /// <summary>波動 0~1。越高速度起伏越大，容易爆冷也容易崩盤。</summary>
        public double Volatility = 0.5;

        public HorseConfig Clone()
        {
            return new HorseConfig
            {
                Name = Name,
                ColorHex = ColorHex,
                BaseSpeed = BaseSpeed,
                Stamina = Stamina,
                Volatility = Volatility
            };
        }

        /// <summary>把欄位夾到合理範圍，避免現場手改 JSON 改壞時整個程式炸掉。</summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(Name))
            {
                Name = "無名馬";
            }

            if (string.IsNullOrEmpty(ColorHex) || ColorHex[0] != '#')
            {
                ColorHex = "#FFFFFF";
            }

            BaseSpeed = ConfigMath.Clamp(BaseSpeed, 1.0, 60.0);
            Stamina = ConfigMath.Clamp(Stamina, 0.0, 1.0);
            Volatility = ConfigMath.Clamp(Volatility, 0.0, 1.0);
        }
    }
}
