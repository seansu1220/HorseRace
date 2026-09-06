using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>
    /// 賽事模擬。純 C#、固定步長、可複製狀態，因此同樣的 seed 與同樣的道具輸入序列
    /// 必定得到完全相同的結果——這是賠率模擬、賽後重播與除錯的基礎。
    ///
    /// 每個模擬步長對每匹馬計算：
    ///   目標速度 = 基礎速度 × 耐力係數 × 波動係數 × 道具倍率
    ///   實際速度平滑逼近目標速度，再依此推進距離。
    /// </summary>
    public sealed class RaceEngine
    {
        private readonly RaceConfig _config;
        private readonly HorseState[] _horses;
        private readonly int[] _rankScratch;
        private readonly Comparison<int> _rankComparison;

        private DeterministicRandom _random;
        private double _accumulator;
        private double _elapsedSeconds;
        private int _finishedCount;
        private bool _ranksAssigned;

        public RaceEngine(RaceConfig config, IReadOnlyList<HorseConfig> lineup, int seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            if (lineup == null || lineup.Count == 0)
            {
                throw new ArgumentException("RaceEngine 需要至少一匹馬。", "lineup");
            }

            _config = config;
            Seed = seed;
            _random = new DeterministicRandom(seed);
            _horses = new HorseState[lineup.Count];
            _rankScratch = new int[lineup.Count];
            _rankComparison = CompareByCurrentPosition;

            for (int lane = 0; lane < lineup.Count; lane++)
            {
                _horses[lane] = new HorseState
                {
                    Lane = lane,
                    Config = lineup[lane],
                    Speed = lineup[lane].BaseSpeed * StartingSpeedRatio,
                    Form = DrawForm(config)
                };
            }
        }

        /// <summary>抽出這匹馬本場的「當日狀態」。整場固定，是賽果隨機性的主要來源。</summary>
        private double DrawForm(RaceConfig config)
        {
            double form = 1.0 + _random.NextGaussian() * config.FormSpread;
            return ConfigMath.Clamp(form, MinForm, MaxForm);
        }

        private const double MinForm = 0.75;
        private const double MaxForm = 1.25;

        /// <summary>起跑瞬間就給一部分初速，避免開賽前兩秒畫面像在慢慢爬。</summary>
        private const double StartingSpeedRatio = 0.55;

        private RaceEngine(RaceEngine source)
        {
            _config = source._config;
            Seed = source.Seed;
            _random = source._random.Clone();
            _accumulator = source._accumulator;
            _elapsedSeconds = source._elapsedSeconds;
            _finishedCount = source._finishedCount;
            _ranksAssigned = source._ranksAssigned;

            _horses = new HorseState[source._horses.Length];
            for (int i = 0; i < _horses.Length; i++)
            {
                _horses[i] = source._horses[i].Clone();
            }

            _rankScratch = new int[_horses.Length];
            _rankComparison = CompareByCurrentPosition;
        }

        public int Seed { get; private set; }

        public double TrackLengthMeters
        {
            get { return _config.TrackLengthMeters; }
        }

        public double ElapsedSeconds
        {
            get { return _elapsedSeconds; }
        }

        public IReadOnlyList<HorseState> Horses
        {
            get { return _horses; }
        }

        public int HorseCount
        {
            get { return _horses.Length; }
        }

        public bool IsFinished
        {
            get { return _finishedCount >= _horses.Length; }
        }

        /// <summary>
        /// 以外部經過的時間推進模擬。內部走固定步長累加器，多餘的時間留到下一次呼叫，
        /// 因此畫面幀率高低不會影響賽果。
        /// </summary>
        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0 || IsFinished)
            {
                return;
            }

            // 視窗被拖曳或載入卡頓後，deltaTime 可能高達數秒。
            // 不設上限的話會一次補上百步，畫面直接跳過半場比賽。
            if (deltaSeconds > _config.MaxCatchUpSeconds)
            {
                deltaSeconds = _config.MaxCatchUpSeconds;
            }

            _accumulator += deltaSeconds;

            double step = _config.FixedStepSeconds;
            while (_accumulator >= step && !IsFinished)
            {
                Step(step);
                _accumulator -= step;
            }
        }

        /// <summary>一路跑到終點。供賠率模擬使用，不經過累加器。</summary>
        public void RunToCompletion()
        {
            double step = _config.FixedStepSeconds;
            while (!IsFinished)
            {
                Step(step);
            }
        }

        /// <summary>
        /// 對某匹馬施加速度效果。已完賽或閘號無效時直接忽略（回傳 false），
        /// 不拋例外——這條路徑上的輸入來自手機端，不能讓壞資料中斷賽事。
        /// </summary>
        public bool ApplyEffect(int lane, SpeedEffect effect)
        {
            if (effect == null || lane < 0 || lane >= _horses.Length)
            {
                return false;
            }

            HorseState horse = _horses[lane];
            if (horse.Finished || effect.RemainingSeconds <= 0.0)
            {
                return false;
            }

            horse.Effects.Add(effect);
            return true;
        }

        /// <summary>目前這匹馬身上有幾個生效中的效果。用於「同一匹最多疊 N 層」的驗證。</summary>
        public int ActiveEffectCount(int lane)
        {
            if (lane < 0 || lane >= _horses.Length)
            {
                return 0;
            }

            return _horses[lane].Effects.Count;
        }

        public RaceEngine Clone()
        {
            return new RaceEngine(this);
        }

        /// <summary>
        /// 依名次填入每個閘號目前的名次（1 起算）。
        /// 傳入重複使用的陣列，避免賽中每幀配置記憶體。
        /// </summary>
        public void FillLiveRanks(int[] ranksByLane)
        {
            if (ranksByLane == null || ranksByLane.Length < _horses.Length)
            {
                throw new ArgumentException(
                    "FillLiveRanks 需要長度至少等於馬匹數的陣列。", "ranksByLane");
            }

            for (int i = 0; i < _horses.Length; i++)
            {
                _rankScratch[i] = i;
            }

            Array.Sort(_rankScratch, _rankComparison);

            for (int position = 0; position < _rankScratch.Length; position++)
            {
                ranksByLane[_rankScratch[position]] = position + 1;
            }
        }

        /// <summary>
        /// 依完賽時間排序的閘號陣列，索引 0 是冠軍。
        /// 呼叫時若比賽已結束，會順便把名次寫回各匹馬的 <see cref="HorseState.FinishRank"/>。
        /// </summary>
        public int[] GetFinishOrder()
        {
            int[] order = new int[_horses.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, _rankComparison);

            if (IsFinished && !_ranksAssigned)
            {
                for (int position = 0; position < order.Length; position++)
                {
                    _horses[order[position]].FinishRank = position + 1;
                }

                _ranksAssigned = true;
            }

            return order;
        }

        /// <summary>冠軍閘號。比賽尚未結束時回傳目前領先者。</summary>
        public int WinnerLane
        {
            get
            {
                int best = 0;
                for (int lane = 1; lane < _horses.Length; lane++)
                {
                    if (CompareByCurrentPosition(lane, best) < 0)
                    {
                        best = lane;
                    }
                }

                return best;
            }
        }

        // ---- 內部實作 ----

        private void Step(double dt)
        {
            _elapsedSeconds += dt;

            for (int lane = 0; lane < _horses.Length; lane++)
            {
                HorseState horse = _horses[lane];
                if (horse.Finished)
                {
                    continue;
                }

                ExpireEffects(horse, dt);

                double targetSpeed = ComputeTargetSpeed(horse, dt);
                horse.Speed += (targetSpeed - horse.Speed) * _config.SpeedSmoothing * dt;
                if (horse.Speed < 0.0)
                {
                    horse.Speed = 0.0;
                }

                horse.Distance += horse.Speed * dt;

                if (horse.Distance >= _config.TrackLengthMeters)
                {
                    MarkFinished(horse);
                }
                else
                {
                    horse.Progress01 = horse.Distance / _config.TrackLengthMeters;
                }
            }

            if (!IsFinished && _elapsedSeconds >= _config.MaxRaceSeconds)
            {
                ForceFinishStragglers();
            }
        }

        private double ComputeTargetSpeed(HorseState horse, double dt)
        {
            HorseConfig stats = horse.Config;

            // 耐力：越接近終點掉速越明顯，耐力高的馬掉得少
            double fatigue = FatigueAt(horse.Progress01);
            double staminaFactor = 1.0 - _config.MaxFatiguePenalty * fatigue * (1.0 - stats.Stamina);

            // 波動：均值回歸過程，比每步獨立取樣的白雜訊平順，能自然做出超車與被追上
            double sigma = _config.NoiseScale * stats.Volatility;
            horse.Noise += -_config.NoiseReversion * horse.Noise * dt
                           + sigma * Math.Sqrt(dt) * _random.NextGaussian();

            double noiseFactor = 1.0 + horse.Noise;
            if (noiseFactor < MinNoiseFactor)
            {
                noiseFactor = MinNoiseFactor; // 波動再差也不能讓馬倒退或完全停住
            }

            double itemFactor = 1.0;
            List<SpeedEffect> effects = horse.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                itemFactor *= effects[i].Multiplier;
            }

            return stats.BaseSpeed * horse.Form * staminaFactor * noiseFactor * itemFactor;
        }

        private const double MinNoiseFactor = 0.35;

        private double FatigueAt(double progress01)
        {
            double start = _config.FatigueStart;
            if (progress01 <= start)
            {
                return 0.0;
            }

            double t = (progress01 - start) / (1.0 - start);
            return t * t;
        }

        private static void ExpireEffects(HorseState horse, double dt)
        {
            List<SpeedEffect> effects = horse.Effects;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                effects[i].RemainingSeconds -= dt;
                if (effects[i].RemainingSeconds <= 0.0)
                {
                    effects.RemoveAt(i);
                }
            }
        }

        private void MarkFinished(HorseState horse)
        {
            // 用超出終點的距離回推衝線的精確時刻，同步衝線也分得出前後
            double overshoot = horse.Distance - _config.TrackLengthMeters;
            double rewind = horse.Speed > 1e-9 ? overshoot / horse.Speed : 0.0;

            horse.FinishTime = _elapsedSeconds - rewind;
            horse.Distance = _config.TrackLengthMeters;
            horse.Progress01 = 1.0;
            horse.Speed = 0.0;
            horse.Finished = true;
            horse.Effects.Clear();
            _finishedCount++;
        }

        /// <summary>安全閥：模擬超過時間上限時，把還沒跑完的馬依當下距離直接判定完賽。</summary>
        private void ForceFinishStragglers()
        {
            for (int lane = 0; lane < _horses.Length; lane++)
            {
                HorseState horse = _horses[lane];
                if (horse.Finished)
                {
                    continue;
                }

                // 距離越遠者名次越前，所以完賽時間用「剩餘距離」單調遞增地編出來
                double remaining = _config.TrackLengthMeters - horse.Distance;
                horse.FinishTime = _elapsedSeconds + remaining;
                horse.Speed = 0.0;
                horse.Finished = true;
                horse.Effects.Clear();
                _finishedCount++;
            }
        }

        /// <summary>名次比較：已完賽者依完賽時間，未完賽者依已跑距離，完賽者一律在前。</summary>
        private int CompareByCurrentPosition(int laneA, int laneB)
        {
            HorseState a = _horses[laneA];
            HorseState b = _horses[laneB];

            if (a.Finished && b.Finished)
            {
                int byTime = a.FinishTime.CompareTo(b.FinishTime);
                return byTime != 0 ? byTime : laneA.CompareTo(laneB);
            }

            if (a.Finished)
            {
                return -1;
            }

            if (b.Finished)
            {
                return 1;
            }

            int byDistance = b.Distance.CompareTo(a.Distance);
            return byDistance != 0 ? byDistance : laneA.CompareTo(laneB);
        }
    }
}
