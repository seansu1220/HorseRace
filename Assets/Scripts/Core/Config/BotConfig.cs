using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 電腦玩家（測試用）的行為參數。只影響電腦玩家怎麼玩，不影響任何遊戲規則。
    /// </summary>
    [Serializable]
    public sealed class BotConfig
    {
        /// <summary>每場下注的機率。</summary>
        public double BetChance = 0.9;

        /// <summary>每次下注最多押手上籌碼的幾成。</summary>
        public double MaxBetFraction = 0.3;

        /// <summary>每場加入啦啦隊的機率。</summary>
        public double CheerChance = 0.7;

        /// <summary>下注階段開始後，在幾秒內隨機挑一個時間點下注與加入啦啦隊。</summary>
        public double DecideWithinSeconds = 8.0;

        /// <summary>比賽中平均每秒用幾張券。</summary>
        public double ItemsPerSecond = 0.35;

        /// <summary>有加入啦啦隊時，平均每秒搖幾步（會再經過每人每秒上限過濾）。</summary>
        public double StepsPerSecond = 7.0;

        /// <summary>最多幾個電腦玩家，避免按住按鍵加爆。</summary>
        public int MaxBots = 40;

        public void Validate()
        {
            BetChance = ConfigMath.Clamp(BetChance, 0.0, 1.0);
            MaxBetFraction = ConfigMath.Clamp(MaxBetFraction, 0.01, 1.0);
            CheerChance = ConfigMath.Clamp(CheerChance, 0.0, 1.0);
            DecideWithinSeconds = ConfigMath.Clamp(DecideWithinSeconds, 0.0, 120.0);
            ItemsPerSecond = ConfigMath.Clamp(ItemsPerSecond, 0.0, 20.0);
            StepsPerSecond = ConfigMath.Clamp(StepsPerSecond, 0.0, 100.0);
            MaxBots = ConfigMath.Clamp(MaxBots, 0, 500);
        }
    }
}
