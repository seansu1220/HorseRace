using System.Collections.Generic;
using HorseRace.Core.Protocol;

namespace HorseRace.Core
{
    /// <summary>
    /// 一個電腦玩家（測試多人情境用）的決策。產出的是和手機完全相同的上行訊息，
    /// 由大螢幕走同一條處理路徑，所以測到的就是真人會走的流程。
    ///
    /// 純函式風格：不碰網路也不讀時鐘，時間由呼叫端以 dt 餵入，亂數用 <see cref="DeterministicRandom"/>，
    /// 同樣的 seed 與輸入會做出同樣的決定。
    /// </summary>
    public sealed class BotBrain
    {
        private static readonly List<InboundMessage> NoActions = new List<InboundMessage>();

        private readonly BotConfig _config;
        private readonly DeterministicRandom _random;

        private bool _hasPhase;
        private RacePhase _phase;
        private double _phaseElapsed;
        private double _decideAt;
        private bool _decided;
        private int _driveLane = -1;
        private double _owedSteps;

        public BotBrain(string playerId, string nickname, BotConfig config, int seed)
        {
            PlayerId = playerId;
            Nickname = nickname;
            _config = config;
            _random = new DeterministicRandom(seed);
        }

        public string PlayerId { get; private set; }
        public string Nickname { get; private set; }

        public InboundMessage JoinMessage()
        {
            return new InboundMessage { t = MessageType.Join, pid = PlayerId, nick = Nickname };
        }

        /// <summary>
        /// 推進 dt 秒，回傳這段時間內要送出的訊息（多數時候是空的）。
        /// </summary>
        /// <param name="account">這個電腦玩家的帳戶（讀餘額與注單）。</param>
        /// <param name="isCheering">這一場是否已加入啦啦隊。</param>
        public List<InboundMessage> Think(
            RacePhase phase, PlayerAccount account, bool isCheering, int laneCount, double dt)
        {
            if (!_hasPhase || phase != _phase)
            {
                EnterPhase(phase, account, laneCount);
            }

            _phaseElapsed += dt;

            if (account == null || laneCount <= 0)
            {
                return NoActions;
            }

            if (phase == RacePhase.Betting)
            {
                return DecideBetting(account, laneCount);
            }

            if (phase == RacePhase.Racing)
            {
                return ActDuringRace(account, isCheering, laneCount, dt);
            }

            return NoActions;
        }

        private void EnterPhase(RacePhase phase, PlayerAccount account, int laneCount)
        {
            _hasPhase = true;
            _phase = phase;
            _phaseElapsed = 0.0;
            _decided = false;
            _decideAt = _random.NextDouble() * _config.DecideWithinSeconds;
            _owedSteps = 0.0;

            if (phase == RacePhase.Racing)
            {
                int mine = BiggestBetLane(account);
                _driveLane = mine >= 0 ? mine : (laneCount > 0 ? _random.NextInt(laneCount) : -1);
            }
        }

        private List<InboundMessage> DecideBetting(PlayerAccount account, int laneCount)
        {
            if (_decided || _phaseElapsed < _decideAt)
            {
                return NoActions;
            }

            _decided = true;
            List<InboundMessage> actions = new List<InboundMessage>();
            int balance = account.Balance;

            if (_random.NextDouble() < _config.CheerChance)
            {
                actions.Add(new InboundMessage { t = MessageType.Cheer, pid = PlayerId, nick = Nickname });
            }

            if (balance > 0 && _random.NextDouble() < _config.BetChance)
            {
                int amount = (int)(balance * _config.MaxBetFraction * (0.2 + 0.8 * _random.NextDouble()));
                actions.Add(new InboundMessage
                {
                    t = MessageType.Bet, pid = PlayerId, nick = Nickname,
                    lane = _random.NextInt(laneCount), amount = amount < 1 ? 1 : amount
                });
            }

            return actions;
        }

        private List<InboundMessage> ActDuringRace(PlayerAccount account, bool isCheering, int laneCount, double dt)
        {
            List<InboundMessage> actions = null;

            if (_random.NextDouble() < _config.ItemsPerSecond * dt)
            {
                actions = new List<InboundMessage> { PickItem(laneCount) };
            }

            if (isCheering && _driveLane >= 0)
            {
                // 每步的節奏有快有慢，看起來比較像真人在搖
                _owedSteps += _config.StepsPerSecond * dt * (0.6 + 0.8 * _random.NextDouble());
                int steps = (int)_owedSteps;
                if (steps > 0)
                {
                    _owedSteps -= steps;
                    actions = actions ?? new List<InboundMessage>();
                    actions.Add(new InboundMessage
                    {
                        t = MessageType.Step, pid = PlayerId, lane = _driveLane, n = steps
                    });
                }
            }

            return actions ?? NoActions;
        }

        /// <summary>加速多半給自己押的馬，減速與障礙丟給別匹。</summary>
        private InboundMessage PickItem(int laneCount)
        {
            double roll = _random.NextDouble();
            ItemKind kind = roll < 0.45 ? ItemKind.Boost : (roll < 0.8 ? ItemKind.Slow : ItemKind.Obstacle);

            int lane = _random.NextInt(laneCount);
            if (kind == ItemKind.Boost && _driveLane >= 0)
            {
                lane = _driveLane;
            }
            else if (kind != ItemKind.Boost && lane == _driveLane && laneCount > 1)
            {
                lane = (lane + 1 + _random.NextInt(laneCount - 1)) % laneCount;
            }

            return new InboundMessage
            {
                t = MessageType.Item, pid = PlayerId, nick = Nickname, kind = ItemKinds.ToWire(kind), lane = lane
            };
        }

        private static int BiggestBetLane(PlayerAccount account)
        {
            if (account == null)
            {
                return -1;
            }

            int best = -1;
            int bestAmount = 0;
            foreach (Bet bet in account.Bets)
            {
                if (bet.Amount > bestAmount)
                {
                    bestAmount = bet.Amount;
                    best = bet.Lane;
                }
            }

            return best;
        }
    }
}
