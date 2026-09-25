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

        /// <summary>
        /// 同一名玩家、同一種券兩次購買之間的冷卻秒數（加速券與減速券各自計算）。
        /// 設 0 代表與 <see cref="DurationSeconds"/> 相同：效果一結束就能再買。
        /// </summary>
        public double CooldownSeconds = 0.0;

        /// <summary>每張券的價格（籌碼）。</summary>
        public int Cost = 5;

        /// <summary>每名玩家每場最多可買幾張券；0 代表不限。</summary>
        public int UsesPerRace = 0;

        /// <summary>同一匹馬身上最多能同時疊幾個效果，避免全場圍剿一匹。</summary>
        public int MaxStacksPerHorse = 2;

        // ---- 障礙券：放在目標馬前方，撞到後原地停住一段時間 ----

        /// <summary>障礙券的價格。</summary>
        public int ObstacleCost = 10;

        /// <summary>撞到障礙物後完全停住的秒數。</summary>
        public double ObstacleStunSeconds = 2.0;

        /// <summary>障礙物放在目標馬前方幾公尺。太近看不到障礙物出現，太遠要等很久才撞到。</summary>
        public double ObstacleLeadMeters = 10.0;

        /// <summary>
        /// 馬恢復跑動後，幾秒內不能再對牠放障礙物。
        /// 券便宜又能連買，沒有這段保護期，全場輪流放障礙會讓一匹馬整場動不了。
        /// </summary>
        public double ObstacleImmunitySeconds = 3.0;

        /// <summary>同一名玩家兩次購買障礙券的冷卻；0 代表與停住秒數相同。</summary>
        public double ObstacleCooldownSeconds = 0.0;

        /// <summary>這種券的價格。</summary>
        public int CostOf(ItemKind kind)
        {
            return kind == ItemKind.Obstacle ? ObstacleCost : Cost;
        }

        /// <summary>這種券實際生效的冷卻秒數（把「0＝與效果時間相同」展開）。</summary>
        public double CooldownOf(ItemKind kind)
        {
            if (kind == ItemKind.Obstacle)
            {
                return ObstacleCooldownSeconds > 0.0 ? ObstacleCooldownSeconds : ObstacleStunSeconds;
            }

            return EffectiveCooldownSeconds;
        }

        /// <summary>實際生效的冷卻秒數（把「0＝與效果時間相同」展開）。</summary>
        public double EffectiveCooldownSeconds
        {
            get { return CooldownSeconds > 0.0 ? CooldownSeconds : DurationSeconds; }
        }

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
            ObstacleCost = ConfigMath.Clamp(ObstacleCost, 0, 1000000);
            ObstacleStunSeconds = ConfigMath.Clamp(ObstacleStunSeconds, 0.2, 10.0);
            ObstacleLeadMeters = ConfigMath.Clamp(ObstacleLeadMeters, 1.0, 100.0);
            ObstacleImmunitySeconds = ConfigMath.Clamp(ObstacleImmunitySeconds, 0.0, 60.0);
            ObstacleCooldownSeconds = ConfigMath.Clamp(ObstacleCooldownSeconds, 0.0, 120.0);
        }
    }
}
