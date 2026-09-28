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

        /// <summary>
        /// 同一匹馬身上最多能同時疊幾個加速／減速效果；0 代表不限（預設）。
        /// 效果的倍率是相乘的，不限時全場一起丟減速券可以讓一匹馬幾乎停住——這是刻意保留的玩法，
        /// 現場若覺得太狠，設個上限即可，不必改程式。
        /// </summary>
        public int MaxStacksPerHorse = 0;

        // ---- 障礙券：放在目標馬前方，撞到後原地停住一段時間 ----

        /// <summary>障礙券的價格。</summary>
        public int ObstacleCost = 10;

        /// <summary>撞到障礙物後完全停住的秒數。</summary>
        public double ObstacleStunSeconds = 2.0;

        /// <summary>障礙物放在目標馬前方幾公尺。太近看不到障礙物出現，太遠要等很久才撞到。</summary>
        public double ObstacleLeadMeters = 10.0;

        /// <summary>
        /// 同一匹馬前方的障礙物彼此至少要相隔幾公尺。障礙物一律放在馬前方固定距離，
        /// 所以效果等於「馬要再往前跑這麼遠才能放下一個」：避免好幾個疊在同一點，
        /// 撞一次卻消耗掉好幾張券。
        /// </summary>
        public double ObstacleMinSpacingMeters = 1.0;

        /// <summary>
        /// 馬被絆住期間與恢復跑動後幾秒內，不能再對牠放障礙物。預設 0（關閉）。
        /// 現場若發現某匹馬被全場輪流放障礙、整場動不了，把這個數字調大即可，不必改程式。
        /// </summary>
        public double ObstacleImmunitySeconds = 0.0;

        /// <summary>同一名玩家兩次購買障礙券的冷卻；0 代表與停住秒數相同。</summary>
        public double ObstacleCooldownSeconds = 0.0;

        // ---- 啦啦隊：下注階段加入，比賽中才能搖手機出力 ----

        /// <summary>加入啦啦隊的價格。每一場都要重新加入。</summary>
        public int CheerCost = 5;

        /// <summary>
        /// 是否一定要加入啦啦隊才能搖手機出力。關掉就回到「人人都能搖」（例如使用實體計步器時）。
        /// </summary>
        public bool CheerRequired = true;

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
            MaxStacksPerHorse = ConfigMath.Clamp(MaxStacksPerHorse, 0, 999);
            ObstacleCost = ConfigMath.Clamp(ObstacleCost, 0, 1000000);
            CheerCost = ConfigMath.Clamp(CheerCost, 0, 1000000);
            ObstacleStunSeconds = ConfigMath.Clamp(ObstacleStunSeconds, 0.2, 10.0);
            ObstacleLeadMeters = ConfigMath.Clamp(ObstacleLeadMeters, 1.0, 100.0);
            ObstacleMinSpacingMeters = ConfigMath.Clamp(ObstacleMinSpacingMeters, 0.0, 50.0);
            ObstacleImmunitySeconds = ConfigMath.Clamp(ObstacleImmunitySeconds, 0.0, 60.0);
            ObstacleCooldownSeconds = ConfigMath.Clamp(ObstacleCooldownSeconds, 0.0, 120.0);
        }
    }
}
