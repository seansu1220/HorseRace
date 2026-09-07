using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 賽事的所有平衡性數值。程式碼中不得出現這裡以外的魔術數字。
    /// 現場調參數靠改 StreamingAssets/config/race.json，不需重新編譯。
    /// </summary>
    [Serializable]
    public sealed class RaceConfig
    {
        // ---- 賽道 ----

        /// <summary>賽道總長（公尺）。450 公尺約對應 26 秒的賽事長度。</summary>
        public double TrackLengthMeters = 450.0;

        /// <summary>單場模擬的安全上限（秒）。超過就強制收尾，避免參數改壞時無限迴圈。</summary>
        public double MaxRaceSeconds = 120.0;

        // ---- 模擬 ----

        /// <summary>固定模擬步長（秒）。0.02 = 50 Hz。改動此值會改變所有既有 seed 的結果。</summary>
        public double FixedStepSeconds = 0.02;

        /// <summary>單次 Advance 最多消化多少秒，避免卡頓後一次補上百步（death spiral）。</summary>
        public double MaxCatchUpSeconds = 0.25;

        /// <summary>速度逼近目標值的係數（每秒）。越大越靈敏，越小越有慣性。</summary>
        public double SpeedSmoothing = 2.5;

        /// <summary>從賽程幾成開始出現疲勞掉速。</summary>
        public double FatigueStart = 0.55;

        /// <summary>耐力為 0 的馬，終點前最多掉速幾成。</summary>
        public double MaxFatiguePenalty = 0.22;

        /// <summary>速度波動的均值回歸速率。越大則波動越短促。</summary>
        public double NoiseReversion = 1.5;

        /// <summary>速度波動的強度基準，實際強度再乘上該匹馬的 Volatility。</summary>
        public double NoiseScale = 0.55;

        /// <summary>每場對基礎速度的隨機微調幅度，讓同一批馬每場賠率都不同。</summary>
        public double StatJitter = 0.05;

        /// <summary>
        /// 開賽前替每匹馬抽一次「當日狀態」的幅度，整場固定不變。
        ///
        /// 這是賽果隨機性的主要來源，也是能不能好玩的關鍵旋鈕。
        /// 只靠 NoiseScale 的即時波動不夠：波動會均值回歸，26 秒的賽程等於取了幾十次獨立取樣，
        /// 平均下來幾乎抵銷，結果就是「基礎速度最快的那匹幾乎每場都贏」，賠率會爛到 1.1 倍。
        /// 當日狀態是整場的固定偏移，不會被平均掉，能真正把弱馬的勝率拉起來。
        /// 數值需與馬匹之間的實力差距同量級（預設名冊的差距約 3.5%）。
        /// </summary>
        public double FormSpread = 0.045;

        // ---- 體力驅動（實體計步器／手機搖動）----

        /// <summary>
        /// 搖動能提供的最大速度加成。0.6 = 全力搖時比基準快六成。
        ///
        /// 這是「體力占多少比重」的總旋鈕，也是唯一需要現場調的數字：
        /// 調到 0.15 搖動只是調味，賠率幾乎準確；調到 1.5 就接近純體力賽，
        /// 賠率只剩參考價值。刻意做成單一參數，現場覺得不夠刺激直接改。
        /// </summary>
        public double MaxDriveBonus = 0.6;

        /// <summary>每秒幾步算「全力」。超過這個步頻不會再更快，擋掉狂甩與機械輔助。</summary>
        public double StepsPerSecondForFullDrive = 6.0;

        /// <summary>
        /// 停止搖動後驅動強度的衰減時間常數（秒）。
        /// 同時也是容錯機制：裝置沒電或斷線時，驅動強度會自己滑回 0，
        /// 那匹馬退回基準速度繼續跑完，不會卡在半路。
        /// </summary>
        public double DriveDecaySeconds = 1.0;

        // ---- 階段秒數 ----

        /// <summary>待機展示。賠率的蒙地卡羅模擬就在這段時間於背景算完。</summary>
        public double IdleSeconds = 6.0;

        /// <summary>下注倒數。</summary>
        public double BettingSeconds = 30.0;

        /// <summary>衝線特寫與名次揭曉。</summary>
        public double PhotoSeconds = 4.0;

        /// <summary>派彩結算。</summary>
        public double SettleSeconds = 8.0;

        // ---- 籌碼與賠率 ----

        /// <summary>玩家進場的初始籌碼。</summary>
        public int StartingChips = 1000;

        /// <summary>單筆最低下注額。</summary>
        public int MinimumBet = 50;

        /// <summary>
        /// 同情籌碼：每場開賽前，籌碼低於這個數字的玩家會被補到這個數字。
        ///
        /// 三十人的聚會裡若有人第三場就輸光、之後只能乾坐著，場子就冷掉一角。
        /// 設 0 可關閉這個機制。
        /// </summary>
        public int CharityChips = 200;

        /// <summary>抽水率。賠率整體的平衡閥門，調高則玩家長期期望值下降。</summary>
        public double TakeRate = 0.15;

        /// <summary>賠率蒙地卡羅的模擬次數。1500 次在一般 PC 上約需數百毫秒。</summary>
        public int OddsSimulationRuns = 1500;

        /// <summary>賠率下限。避免熱門馬出現小於 1 的賠率。</summary>
        public double MinOdds = 1.05;

        /// <summary>賠率上限。避免弱馬賠率爆表。</summary>
        public double MaxOdds = 20.0;

        public RaceConfig Clone()
        {
            return (RaceConfig)MemberwiseClone();
        }

        /// <summary>把欄位夾到合理範圍。任何從外部 JSON 讀進來的設定都必須先過這關。</summary>
        public void Validate()
        {
            TrackLengthMeters = ConfigMath.Clamp(TrackLengthMeters, 50.0, 5000.0);
            MaxRaceSeconds = ConfigMath.Clamp(MaxRaceSeconds, 10.0, 600.0);

            FixedStepSeconds = ConfigMath.Clamp(FixedStepSeconds, 0.002, 0.1);
            MaxCatchUpSeconds = ConfigMath.Clamp(MaxCatchUpSeconds, FixedStepSeconds, 2.0);
            SpeedSmoothing = ConfigMath.Clamp(SpeedSmoothing, 0.1, 50.0);
            FatigueStart = ConfigMath.Clamp(FatigueStart, 0.0, 0.95);
            MaxFatiguePenalty = ConfigMath.Clamp(MaxFatiguePenalty, 0.0, 0.8);
            NoiseReversion = ConfigMath.Clamp(NoiseReversion, 0.01, 20.0);
            NoiseScale = ConfigMath.Clamp(NoiseScale, 0.0, 3.0);
            StatJitter = ConfigMath.Clamp(StatJitter, 0.0, 0.5);
            FormSpread = ConfigMath.Clamp(FormSpread, 0.0, 0.3);

            MaxDriveBonus = ConfigMath.Clamp(MaxDriveBonus, 0.0, 3.0);
            StepsPerSecondForFullDrive = ConfigMath.Clamp(StepsPerSecondForFullDrive, 0.5, 30.0);
            DriveDecaySeconds = ConfigMath.Clamp(DriveDecaySeconds, 0.1, 10.0);

            IdleSeconds = ConfigMath.Clamp(IdleSeconds, 0.5, 120.0);
            BettingSeconds = ConfigMath.Clamp(BettingSeconds, 3.0, 600.0);
            PhotoSeconds = ConfigMath.Clamp(PhotoSeconds, 0.5, 60.0);
            SettleSeconds = ConfigMath.Clamp(SettleSeconds, 0.5, 120.0);

            StartingChips = ConfigMath.Clamp(StartingChips, 1, 1000000);
            MinimumBet = ConfigMath.Clamp(MinimumBet, 1, StartingChips);
            CharityChips = ConfigMath.Clamp(CharityChips, 0, StartingChips);
            TakeRate = ConfigMath.Clamp(TakeRate, 0.0, 0.5);
            OddsSimulationRuns = ConfigMath.Clamp(OddsSimulationRuns, 50, 50000);
            MinOdds = ConfigMath.Clamp(MinOdds, 1.0, 100.0);
            MaxOdds = ConfigMath.Clamp(MaxOdds, MinOdds, 1000.0);
        }
    }
}
