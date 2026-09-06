using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>單匹馬在一場比賽中的執行期狀態。由 <see cref="RaceEngine"/> 獨佔寫入。</summary>
    public sealed class HorseState
    {
        /// <summary>尚未完賽的馬，其 FinishTime 一律是這個值。</summary>
        public const double NotFinishedTime = double.MaxValue;

        /// <summary>起跑閘號，同時也是所有陣列與訊息協定使用的索引。</summary>
        public int Lane;

        /// <summary>本場的能力值（已含每場微調）。比賽期間不會被改動。</summary>
        public HorseConfig Config;

        /// <summary>已跑距離（公尺）。</summary>
        public double Distance;

        /// <summary>目前速度（公尺／秒）。</summary>
        public double Speed;

        /// <summary>賽程完成度 0~1，由引擎每步更新，供顯示層直接取用。</summary>
        public double Progress01;

        /// <summary>速度波動的內部狀態（均值回歸過程）。</summary>
        public double Noise;

        /// <summary>本場的「當日狀態」倍率，開賽前抽定後整場不變。1.0 代表正常發揮。</summary>
        public double Form = 1.0;

        public bool Finished;

        /// <summary>完賽時間（秒）。以次步長精度內插，讓同步衝線也分得出前後。</summary>
        public double FinishTime = NotFinishedTime;

        /// <summary>最終名次，1 起算。未結算前為 0。</summary>
        public int FinishRank;

        /// <summary>目前生效中的道具效果。</summary>
        public readonly List<SpeedEffect> Effects = new List<SpeedEffect>();

        public HorseState Clone()
        {
            HorseState copy = new HorseState
            {
                Lane = Lane,
                Config = Config, // 比賽期間不變動，共用同一份即可
                Distance = Distance,
                Speed = Speed,
                Progress01 = Progress01,
                Noise = Noise,
                Form = Form,
                Finished = Finished,
                FinishTime = FinishTime,
                FinishRank = FinishRank
            };

            for (int i = 0; i < Effects.Count; i++)
            {
                copy.Effects.Add(Effects[i].Clone());
            }

            return copy;
        }
    }
}
