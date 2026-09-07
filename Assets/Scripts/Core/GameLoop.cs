using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>一場賽事的階段。手機端與大螢幕共用同一組定義。</summary>
    public enum RacePhase
    {
        /// <summary>待機。展示上一場結果，同時在背景算下一場的賠率。</summary>
        Idle = 0,

        /// <summary>下注倒數。</summary>
        Betting = 1,

        /// <summary>賽事進行中，可使用道具。</summary>
        Racing = 2,

        /// <summary>衝線特寫與名次揭曉。</summary>
        Photo = 3,

        /// <summary>派彩結算。</summary>
        Settle = 4
    }

    /// <summary>
    /// 賽事階段狀態機。整個遊戲對外只有這一個進入點：
    /// 外部負責餵時間（<see cref="Tick"/>）與注入賠率（<see cref="SetOdds"/>），
    /// 其餘一律由這裡決定。純 C#，不碰 Unity 也不碰網路。
    /// </summary>
    public sealed class GameLoop
    {
        private readonly GameConfig _config;
        private readonly DeterministicRandom _seedSource;

        private double _phaseRemaining;

        public GameLoop(GameConfig config, int seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
            _seedSource = new DeterministicRandom(seed);
            Book = new BettingBook(config.Race);
            RaceNumber = 1;

            PrepareLineup();
            EnterPhase(RacePhase.Idle);
        }

        /// <summary>目前階段。</summary>
        public RacePhase Phase { get; private set; }

        /// <summary>本階段剩餘秒數。Racing 階段沒有倒數，這裡固定為 0。</summary>
        public double PhaseRemainingSeconds
        {
            get { return _phaseRemaining < 0.0 ? 0.0 : _phaseRemaining; }
        }

        /// <summary>第幾場，1 起算。</summary>
        public int RaceNumber { get; private set; }

        /// <summary>本場出賽名單（已含每場的能力值微調）。</summary>
        public HorseConfig[] Lineup { get; private set; }

        /// <summary>本場賠率。尚未算完時為 null。</summary>
        public double[] Odds { get; private set; }

        /// <summary>本場的模擬用 seed。名單決定的當下就決定，方便賽後重播。</summary>
        public int RaceSeed { get; private set; }

        /// <summary>賽事模擬。只有在 Racing 之後才不是 null。</summary>
        public RaceEngine Race { get; private set; }

        /// <summary>本場的最終名次（閘號陣列，索引 0 是冠軍）。未跑完為 null。</summary>
        public int[] FinishOrder { get; private set; }

        /// <summary>籌碼與注單的帳本。</summary>
        public BettingBook Book { get; private set; }

        /// <summary>上一場的派彩結果。尚未結算過為 null。</summary>
        public SettlementResult LastSettlement { get; private set; }

        /// <summary>
        /// 代為下注。階段檢查放在這裡而不是帳本裡，
        /// 因為「什麼時候可以下注」是流程規則，不是帳務規則。
        /// </summary>
        public BetRejection TryPlaceBet(string playerId, int lane, int amount)
        {
            if (Phase != RacePhase.Betting)
            {
                return BetRejection.NotBettingPhase;
            }

            return Book.TryPlaceBet(playerId, lane, amount, Lineup.Length);
        }

        /// <summary>賠率還沒算好。外部看到 true 就該去啟動背景計算。</summary>
        public bool NeedsOdds
        {
            get { return Odds == null; }
        }

        public RaceConfig RaceConfig
        {
            get { return _config.Race; }
        }

        /// <summary>階段切換通知。參數是「剛進入的」階段。</summary>
        public event Action<RacePhase> PhaseEntered;

        /// <summary>推進時間。外部每幀呼叫一次，傳入這一幀經過的秒數。</summary>
        public void Tick(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
            {
                return;
            }

            if (Phase == RacePhase.Racing)
            {
                Race.Advance(deltaSeconds);
                if (Race.IsFinished)
                {
                    FinishOrder = Race.GetFinishOrder();
                    EnterPhase(RacePhase.Photo);
                }

                return;
            }

            _phaseRemaining -= deltaSeconds;
            if (_phaseRemaining > 0.0)
            {
                return;
            }

            AdvanceToNextPhase();
        }

        /// <summary>注入背景算好的賠率。名單已經換過的話會被忽略。</summary>
        public void SetOdds(int forRaceNumber, double[] odds)
        {
            if (odds == null || forRaceNumber != RaceNumber || odds.Length != Lineup.Length)
            {
                return;
            }

            Odds = odds;
        }

        /// <summary>除錯用：立刻結束目前階段。Racing 階段則直接把比賽跑完。</summary>
        public void SkipPhase()
        {
            if (Phase == RacePhase.Racing)
            {
                Race.RunToCompletion();
                FinishOrder = Race.GetFinishOrder();
                EnterPhase(RacePhase.Photo);
                return;
            }

            _phaseRemaining = 0.0;
            AdvanceToNextPhase();
        }

        // ---- 內部實作 ----

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case RacePhase.Idle:
                    EnterPhase(RacePhase.Betting);
                    break;

                case RacePhase.Betting:
                    Race = new RaceEngine(_config.Race, Lineup, RaceSeed);
                    EnterPhase(RacePhase.Racing);
                    break;

                case RacePhase.Photo:
                    EnterPhase(RacePhase.Settle);
                    break;

                case RacePhase.Settle:
                    RaceNumber++;
                    PrepareLineup();
                    EnterPhase(RacePhase.Idle);
                    break;

                default:
                    // Racing 由 Tick 依比賽是否結束處理，不會走到這裡
                    break;
            }
        }

        private void EnterPhase(RacePhase phase)
        {
            Phase = phase;
            _phaseRemaining = DurationOf(phase);

            if (phase == RacePhase.Betting)
            {
                // 清掉上一場的注單並補發同情籌碼，必須在開放下注之前完成
                Book.BeginRace();
            }
            else if (phase == RacePhase.Settle)
            {
                LastSettlement = Book.Settle(
                    FinishOrder != null && FinishOrder.Length > 0 ? FinishOrder[0] : -1, Odds);
            }

            Action<RacePhase> handler = PhaseEntered;
            if (handler != null)
            {
                handler(phase);
            }
        }

        private double DurationOf(RacePhase phase)
        {
            switch (phase)
            {
                case RacePhase.Idle:
                    return _config.Race.IdleSeconds;
                case RacePhase.Betting:
                    return _config.Race.BettingSeconds;
                case RacePhase.Photo:
                    return _config.Race.PhotoSeconds;
                case RacePhase.Settle:
                    return _config.Race.SettleSeconds;
                default:
                    return 0.0; // Racing 由比賽本身決定長度
            }
        }

        private void PrepareLineup()
        {
            int lineupSeed = (int)(_seedSource.NextULong() & 0x7FFFFFFF);
            RaceSeed = (int)(_seedSource.NextULong() & 0x7FFFFFFF);

            List<HorseConfig> roster = _config.Roster;
            Lineup = RaceLineup.Create(_config.Race, roster, lineupSeed);

            Odds = null;
            Race = null;
            FinishOrder = null;
        }
    }
}
