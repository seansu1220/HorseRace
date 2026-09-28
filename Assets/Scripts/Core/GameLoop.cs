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
        Settle = 4,

        /// <summary>
        /// 開賽前等待入場。不倒數，大螢幕放大顯示 QRCode，主持人按鍵才進入第一場的 Idle。
        /// 編號接在最後，既有階段的數值不變。
        /// </summary>
        Lobby = 5,

        /// <summary>
        /// 遊戲時間到、最後一場打完之後的結束畫面。不再有任何倒數或比賽，按重新開始才會回到等待入場。
        /// </summary>
        GameOver = 6
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

        private readonly StepGate _stepGate;

        private double _phaseRemaining;

        /// <summary>遊戲開始後經過的秒數：只在離開等待入場後、遊戲結束前累計。</summary>
        private double _gameElapsed;
        private double _gameDurationSeconds;
        private double _allowanceElapsed;

        public GameLoop(GameConfig config, int seed)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
            _seedSource = new DeterministicRandom(seed);
            Book = new BettingBook(config.Race);
            Items = new ItemShop(config.Items);
            Cheer = new CheerSquad(config.Items);
            _stepGate = new StepGate(config.Race);
            RaceNumber = 1;
            _gameDurationSeconds = config.Race.GameMinutes * 60.0;

            PrepareLineup();
            EnterPhase(config.Race.WaitForHostToStart ? RacePhase.Lobby : RacePhase.Idle);
        }

        /// <summary>目前階段。</summary>
        public RacePhase Phase { get; private set; }

        /// <summary>遊戲時限（秒）；0 代表不限時。</summary>
        public double GameDurationSeconds
        {
            get { return _gameDurationSeconds; }
        }

        /// <summary>遊戲開始後經過的秒數（等待入場期間不計）。</summary>
        public double GameElapsedSeconds
        {
            get { return _gameElapsed; }
        }

        /// <summary>遊戲時間已到：這一場打完就結束。不限時時永遠是 false。</summary>
        public bool IsTimeUp
        {
            get { return _gameDurationSeconds > 0.0 && _gameElapsed >= _gameDurationSeconds; }
        }

        /// <summary>剩餘遊戲時間（秒），不限時時為 -1。</summary>
        public double GameRemainingSeconds
        {
            get
            {
                if (_gameDurationSeconds <= 0.0)
                {
                    return -1.0;
                }

                double remaining = _gameDurationSeconds - _gameElapsed;
                return remaining > 0.0 ? remaining : 0.0;
            }
        }

        /// <summary>發零用金時通知，參數是每人拿到的金額。</summary>
        public event Action<int> AllowanceGranted;

        /// <summary>主持人設定遊戲時間（分鐘，0 = 不限時）。只能在開賽前的等待入場時設定。</summary>
        public bool SetGameMinutes(double minutes)
        {
            if (Phase != RacePhase.Lobby || minutes < 0.0)
            {
                return false;
            }

            _gameDurationSeconds = minutes * 60.0;
            return true;
        }

        /// <summary>本階段剩餘秒數。Racing 與 Lobby 沒有倒數，這裡固定為 0。</summary>
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

        /// <summary>道具券的購買規則與冷卻。</summary>
        public ItemShop Items { get; private set; }

        /// <summary>啦啦隊：誰這場可以搖手機出力，以及各自出了多少力。</summary>
        public CheerSquad Cheer { get; private set; }

        /// <summary>上一場的獎項（結算時產生，下一場結算前保持不變）。</summary>
        public List<Award> LastAwards { get; private set; }

        /// <summary>加入啦啦隊。只有下注階段可以加入，比賽中才能搖。</summary>
        public CheerRejection TryBuyCheer(string playerId)
        {
            if (Phase != RacePhase.Betting)
            {
                return CheerRejection.NotBetting;
            }

            return Cheer.TryJoin(Book.Find(playerId));
        }

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

        /// <summary>
        /// 代為購買並使用一張道具券。「什麼時候能用」由這裡把關，扣款與冷卻由 <see cref="ItemShop"/> 決定。
        /// </summary>
        public ItemRejection TryUseItem(string playerId, ItemKind kind, int lane)
        {
            if (Phase != RacePhase.Racing || Race == null)
            {
                return ItemRejection.NotRacing;
            }

            return Items.TryUse(Book.Find(playerId), kind, lane, Race);
        }

        /// <summary>這名玩家的這種券還要冷卻幾秒；不在比賽中時為 0。</summary>
        public double ItemCooldownRemaining(string playerId, ItemKind kind)
        {
            return Race == null || Phase != RacePhase.Racing
                ? 0.0
                : Items.CooldownRemaining(playerId, kind, Race.ElapsedSeconds);
        }

        /// <summary>
        /// 替某匹馬累加玩家搖出來的步數。每名玩家每秒有上限（見 <see cref="StepGate"/>），
        /// 超出的部分直接丟掉。回傳實際計入的步數。
        /// </summary>
        public int AddSteps(string playerId, int lane, int stepCount)
        {
            if (Phase != RacePhase.Racing || Race == null)
            {
                return 0;
            }

            if (_config.Items.CheerRequired && !Cheer.IsMember(playerId))
            {
                return 0; // 沒加入啦啦隊的人不能出力
            }

            int admitted = _stepGate.Admit(playerId, stepCount, Race.ElapsedSeconds);
            if (admitted <= 0 || !Race.AddSteps(lane, admitted))
            {
                return 0;
            }

            Cheer.RecordSteps(playerId, admitted);
            return admitted;
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
            if (deltaSeconds <= 0.0 || Phase == RacePhase.Lobby || Phase == RacePhase.GameOver)
            {
                // 等待入場時時間不流動：人還沒到齊，倒數不能偷偷開始；遊戲結束後也不再動
                return;
            }

            AdvanceGameClock(deltaSeconds);

            // 時間到的時候若正在兩場之間，不再開新的一場
            if (Phase == RacePhase.Idle && IsTimeUp)
            {
                EnterPhase(RacePhase.GameOver);
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

        /// <summary>
        /// 主持人宣布開始：從等待入場進入第一場。不在等待入場時呼叫不會有任何效果。
        /// </summary>
        /// <returns>是否真的開始了。</returns>
        public bool StartFromLobby()
        {
            if (Phase != RacePhase.Lobby)
            {
                return false;
            }

            EnterPhase(RacePhase.Idle);
            return true;
        }

        /// <summary>除錯用：立刻結束目前階段。Racing 階段則直接把比賽跑完。</summary>
        public void SkipPhase()
        {
            if (Phase == RacePhase.GameOver)
            {
                return;
            }

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
                case RacePhase.Lobby:
                    EnterPhase(RacePhase.Idle);
                    break;

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
                    if (IsTimeUp)
                    {
                        // 時間已到，這一場就是最後一場
                        EnterPhase(RacePhase.GameOver);
                        break;
                    }

                    RaceNumber++;
                    PrepareLineup();
                    EnterPhase(RacePhase.Idle);
                    break;

                default:
                    // Racing 由 Tick 依比賽是否結束處理，不會走到這裡
                    break;
            }
        }

        /// <summary>推進遊戲時鐘並發零用金。等待入場與遊戲結束時不會被呼叫。</summary>
        private void AdvanceGameClock(double deltaSeconds)
        {
            _gameElapsed += deltaSeconds;

            double interval = _config.Race.AllowanceIntervalSeconds;
            if (interval <= 0.0 || _config.Race.AllowanceChips <= 0)
            {
                return;
            }

            _allowanceElapsed += deltaSeconds;
            while (_allowanceElapsed >= interval)
            {
                _allowanceElapsed -= interval;
                Book.GrantAll(_config.Race.AllowanceChips);

                Action<int> handler = AllowanceGranted;
                if (handler != null)
                {
                    handler(_config.Race.AllowanceChips);
                }
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
                Cheer.BeginBetting();
            }
            else if (phase == RacePhase.Racing)
            {
                // 比賽時間從 0 重新起算，上一場的冷卻與步數額度都不能帶過來
                Items.BeginRace();
                Cheer.BeginRace();
                _stepGate.Reset();
            }
            else if (phase == RacePhase.Settle)
            {
                LastSettlement = Book.Settle(
                    FinishOrder != null && FinishOrder.Length > 0 ? FinishOrder[0] : -1, Odds);

                // 派彩之後才能頒「本場大贏家」
                LastAwards = RaceAwards.Compute(Items.Uses, Cheer, Book);
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
                    return 0.0; // Racing 由比賽本身決定長度；Lobby 等主持人
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
