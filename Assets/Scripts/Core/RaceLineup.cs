using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>
    /// 產生單場的出賽名單。純函式：同樣的 roster + seed 一定得到同樣的名單。
    /// </summary>
    public static class RaceLineup
    {
        /// <summary>
        /// 依名冊產生本場出賽名單，並對基礎速度做小幅隨機微調。
        /// 微調的目的是讓同一批馬每場的賠率都不一樣，否則玩家兩場就背起來了。
        /// </summary>
        public static HorseConfig[] Create(RaceConfig config, IReadOnlyList<HorseConfig> roster, int seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            if (roster == null || roster.Count == 0)
            {
                throw new ArgumentException("RaceLineup.Create 需要至少一匹馬。", "roster");
            }

            DeterministicRandom random = new DeterministicRandom(seed);
            HorseConfig[] lineup = new HorseConfig[roster.Count];

            for (int i = 0; i < roster.Count; i++)
            {
                HorseConfig entry = roster[i].Clone();
                double jitter = random.NextRange(-config.StatJitter, config.StatJitter);
                entry.BaseSpeed *= 1.0 + jitter;
                entry.Validate();
                lineup[i] = entry;
            }

            return lineup;
        }
    }
}
