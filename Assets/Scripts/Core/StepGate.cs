using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>
    /// 限制每名玩家每秒最多計入幾步（令牌桶）。
    ///
    /// 馬的「全力」門檻是所有人加總的步頻；如果不限制個人，一個雙手狂點的人或一支改過的手機
    /// 就能獨自撐滿，「大家一起搖」就沒意義了。桶子容量等於一秒的額度，
    /// 所以短暫停頓後可以補一小段爆發，但長期平均不會超過上限。
    ///
    /// 時間由呼叫端傳入（比賽的模擬時間），本類別不讀時鐘。
    /// </summary>
    public sealed class StepGate
    {
        private readonly RaceConfig _config;
        private readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>();

        private sealed class Bucket
        {
            public double Tokens;
            public double LastTime;
        }

        public StepGate(RaceConfig config)
        {
            _config = config;
        }

        /// <summary>新的一場開跑，比賽時間從 0 重新起算，清掉所有人的桶子。</summary>
        public void Reset()
        {
            _buckets.Clear();
        }

        /// <summary>
        /// 這次回報的步數裡，有幾步可以計入。
        /// </summary>
        /// <param name="playerId">玩家識別碼；舊版手機沒帶時所有匿名回報共用一個桶子。</param>
        /// <param name="requested">手機回報的步數。</param>
        /// <param name="now">比賽的模擬時間（秒）。</param>
        public int Admit(string playerId, int requested, double now)
        {
            if (requested <= 0)
            {
                return 0;
            }

            double rate = _config.MaxStepsPerSecondPerPlayer;
            if (rate <= 0.0)
            {
                return requested;
            }

            Bucket bucket = BucketOf(playerId, rate, now);

            double elapsed = now - bucket.LastTime;
            if (elapsed > 0.0)
            {
                bucket.Tokens = System.Math.Min(rate, bucket.Tokens + elapsed * rate);
                bucket.LastTime = now;
            }

            int admitted = (int)System.Math.Min(requested, System.Math.Floor(bucket.Tokens));
            bucket.Tokens -= admitted;
            return admitted;
        }

        private Bucket BucketOf(string playerId, double rate, double now)
        {
            string key = playerId ?? "";
            Bucket bucket;
            if (!_buckets.TryGetValue(key, out bucket))
            {
                // 新來的人一開始就有一秒的額度，不必先「暖機」
                bucket = new Bucket { Tokens = rate, LastTime = now };
                _buckets[key] = bucket;
            }

            return bucket;
        }
    }
}
