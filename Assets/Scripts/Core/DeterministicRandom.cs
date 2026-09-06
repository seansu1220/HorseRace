using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 可複製內部狀態的決定性亂數產生器（SplitMix64）。
    ///
    /// 之所以不用 <see cref="System.Random"/>：它無法匯出或複製內部狀態，
    /// 而 <see cref="RaceEngine.Clone"/> 必須連亂數狀態一起帶走，
    /// 複本才能跑出與本體完全相同的後續結果——這是賠率模擬與賽後重播的前提。
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        private ulong _state;

        public DeterministicRandom(int seed)
        {
            _state = unchecked((ulong)seed * GoldenGamma + 0x1234567890ABCDEFUL);
        }

        private DeterministicRandom(ulong state)
        {
            _state = state;
        }

        public DeterministicRandom Clone()
        {
            return new DeterministicRandom(_state);
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += GoldenGamma;
                ulong mixed = _state;
                mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
                mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
                return mixed ^ (mixed >> 31);
            }
        }

        /// <summary>[0, 1) 均勻分布。</summary>
        public double NextDouble()
        {
            // 取高 53 位，剛好是 double 的有效位數
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>[minInclusive, maxExclusive) 均勻分布。</summary>
        public double NextRange(double minInclusive, double maxExclusive)
        {
            return minInclusive + NextDouble() * (maxExclusive - minInclusive);
        }

        /// <summary>[0, exclusiveMax) 均勻整數。</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "exclusiveMax", exclusiveMax, "DeterministicRandom.NextInt 的上限必須為正整數。");
            }

            return (int)(NextDouble() * exclusiveMax);
        }

        /// <summary>
        /// 標準常態分布（Box-Muller）。刻意丟棄第二個取樣值，
        /// 讓產生器維持「單一 ulong 狀態」，Clone 才不會漏掉快取的取樣。
        /// </summary>
        public double NextGaussian()
        {
            double uniform = NextDouble();
            if (uniform < 1e-12)
            {
                uniform = 1e-12; // 避免 Log(0)
            }

            double angle = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(uniform)) * Math.Cos(2.0 * Math.PI * angle);
        }
    }
}
