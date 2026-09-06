using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>
    /// 用蒙地卡羅估算各匹馬的勝率，再換算成固定賠率。純函式，不碰任何共用狀態，
    /// 因此可以安全地丟到背景執行緒上跑。
    ///
    /// 為什麼是固定賠率而不是同注分彩：10～30 人的小場次很容易全押同一匹，
    /// 同注分彩會讓賠率掉到 1.1 而失去樂趣。詳見 docs/ARCHITECTURE.md §3。
    /// </summary>
    public static class OddsCalculator
    {
        /// <summary>
        /// 回傳每個閘號的賠率（含本金的倍率）。模擬不含道具，
        /// 因為道具是玩家的變數，不屬於馬匹本身的能力。
        /// </summary>
        public static double[] Compute(RaceConfig config, IReadOnlyList<HorseConfig> lineup, int baseSeed)
        {
            OddsResult result = ComputeDetailed(config, lineup, baseSeed);
            return result.Odds;
        }

        /// <summary>連勝率一起回傳的版本，供測試與除錯顯示使用。</summary>
        public static OddsResult ComputeDetailed(RaceConfig config, IReadOnlyList<HorseConfig> lineup, int baseSeed)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            if (lineup == null || lineup.Count == 0)
            {
                throw new ArgumentException("OddsCalculator 需要至少一匹馬。", "lineup");
            }

            int runs = config.OddsSimulationRuns;
            int[] wins = new int[lineup.Count];

            for (int run = 0; run < runs; run++)
            {
                int seed = unchecked(baseSeed * 31 + run * 7919 + 13);
                RaceEngine simulation = new RaceEngine(config, lineup, seed);
                simulation.RunToCompletion();
                wins[simulation.WinnerLane]++;
            }

            double[] winRates = new double[lineup.Count];
            double[] odds = new double[lineup.Count];
            double payoutPool = 1.0 - config.TakeRate;

            for (int lane = 0; lane < lineup.Count; lane++)
            {
                // 一次都沒贏的馬用「半次」代替，避免除以零導致賠率變成無限大
                double effectiveWins = wins[lane] > 0 ? wins[lane] : 0.5;
                winRates[lane] = (double)wins[lane] / runs;

                double rawOdds = payoutPool / (effectiveWins / runs);
                odds[lane] = Math.Round(
                    ConfigMath.Clamp(rawOdds, config.MinOdds, config.MaxOdds), 2);
            }

            return new OddsResult(odds, winRates, runs);
        }
    }

    /// <summary>賠率計算的完整結果。</summary>
    public sealed class OddsResult
    {
        public OddsResult(double[] odds, double[] winRates, int simulationRuns)
        {
            Odds = odds;
            WinRates = winRates;
            SimulationRuns = simulationRuns;
        }

        /// <summary>各閘號的賠率（含本金）。</summary>
        public double[] Odds { get; private set; }

        /// <summary>各閘號的模擬勝率 0~1。</summary>
        public double[] WinRates { get; private set; }

        public int SimulationRuns { get; private set; }
    }
}
