using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 道具的平衡性數值。首次現場測試後幾乎一定要調，所以一律放設定檔。
    ///
    /// 注意這裡沒有「完全停止」的選項：讓一匹馬定住會使押注該匹的玩家被單方面剝奪，
    /// 小場次很容易演變成互相報復。詳見 docs/ARCHITECTURE.md §4。
    /// </summary>
    [Serializable]
    public sealed class ItemConfig
    {
        /// <summary>加速的速度倍率。</summary>
        public double BoostMultiplier = 1.35;

        /// <summary>絆腳的速度倍率。</summary>
        public double SlowMultiplier = 0.60;

        /// <summary>效果持續秒數。</summary>
        public double DurationSeconds = 2.0;

        /// <summary>同一名玩家兩次使用之間的冷卻秒數。</summary>
        public double CooldownSeconds = 8.0;

        /// <summary>每次使用消耗的籌碼。</summary>
        public int Cost = 50;

        /// <summary>每名玩家每場可使用的次數。</summary>
        public int UsesPerRace = 2;

        /// <summary>同一匹馬身上最多能同時疊幾個效果，避免全場圍剿一匹。</summary>
        public int MaxStacksPerHorse = 2;

        /// <summary>依道具種類取得對應的速度倍率。</summary>
        public double MultiplierFor(EffectKind kind)
        {
            return kind == EffectKind.Boost ? BoostMultiplier : SlowMultiplier;
        }

        public ItemConfig Clone()
        {
            return (ItemConfig)MemberwiseClone();
        }

        public void Validate()
        {
            // 加速永遠要大於 1、減速永遠要小於 1，否則道具的語意會反過來
            BoostMultiplier = ConfigMath.Clamp(BoostMultiplier, 1.01, 3.0);
            SlowMultiplier = ConfigMath.Clamp(SlowMultiplier, 0.2, 0.99);
            DurationSeconds = ConfigMath.Clamp(DurationSeconds, 0.2, 20.0);
            CooldownSeconds = ConfigMath.Clamp(CooldownSeconds, 0.0, 120.0);
            Cost = ConfigMath.Clamp(Cost, 0, 1000000);
            UsesPerRace = ConfigMath.Clamp(UsesPerRace, 0, 99);
            MaxStacksPerHorse = ConfigMath.Clamp(MaxStacksPerHorse, 1, 20);
        }
    }
}
