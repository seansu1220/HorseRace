using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>道具券種類。手機送上來的字串對應到這裡（見 Protocol.ItemKinds），不使用 magic string。</summary>
    public enum ItemKind
    {
        /// <summary>加速券：目標馬短時間加速。</summary>
        Boost = 0,

        /// <summary>減速券：目標馬短時間減速。</summary>
        Slow = 1,

        /// <summary>障礙券：在目標馬前方放一個障礙物，撞到後原地停住。</summary>
        Obstacle = 2
    }

    /// <summary>買券被拒絕的原因。措辭留給呈現層（手機頁）決定。</summary>
    public enum ItemRejection
    {
        None = 0,

        /// <summary>不在比賽中。券只能在 Racing 階段使用。</summary>
        NotRacing = 1,

        /// <summary>沒見過的玩家。</summary>
        UnknownPlayer = 2,

        /// <summary>閘號不存在。</summary>
        InvalidLane = 3,

        /// <summary>那匹馬已經衝線。</summary>
        HorseFinished = 4,

        /// <summary>這種券還在冷卻中（上一張的效果還沒結束）。</summary>
        CoolingDown = 5,

        /// <summary>本場可買的張數用完了（只有設定了上限才會出現）。</summary>
        NoUsesLeft = 6,

        /// <summary>那匹馬身上的加速／減速效果已經疊滿，避免全場圍剿同一匹。</summary>
        HorseEffectsFull = 7,

        /// <summary>籌碼不夠。</summary>
        InsufficientChips = 8,

        /// <summary>那匹馬前方已經有障礙物了。</summary>
        ObstacleAlreadyPlaced = 9,

        /// <summary>那匹馬正被絆住，或剛恢復跑動還在保護期。</summary>
        HorseRecovering = 10,

        /// <summary>離終點太近，放不下障礙物。</summary>
        TooCloseToFinish = 11
    }

    /// <summary>一次成功使用的道具券。賽後統計「誰對哪匹馬用了什麼」。</summary>
    public sealed class ItemUse
    {
        public int Lane;
        public ItemKind Kind;
        public string PlayerId;
        public string Nickname;
    }

    /// <summary>彙整後的一列：某名玩家對某匹馬用了幾張某種券。</summary>
    public sealed class ItemUsageLine
    {
        public int Lane;
        public ItemKind Kind;
        public string PlayerId;
        public string Nickname;
        public int Count;
    }

    /// <summary>
    /// 道具券的購買規則：扣籌碼、冷卻、張數上限、同一匹馬的效果上限，通過後才把效果套到賽事上。
    ///
    /// 冷卻用比賽的模擬時間計算（<see cref="RaceEngine.ElapsedSeconds"/>），而不是牆上時鐘：
    /// 同樣的輸入序列會得到同樣的結果，也不受大螢幕掉幀影響。
    /// 被拒絕時一律不扣錢、不進冷卻。
    /// </summary>
    public sealed class ItemShop
    {
        /// <summary>障礙物至少要離終點這麼遠才放得下，不然還沒看到就衝線了。</summary>
        private const double FinishGuardMeters = 1.0;

        private const int ItemKindCount = 3;

        private readonly ItemConfig _config;
        private readonly Dictionary<string, PlayerItemState> _players = new Dictionary<string, PlayerItemState>();
        private readonly List<ItemUse> _uses = new List<ItemUse>();

        /// <summary>一名玩家本場的道具使用狀態。</summary>
        private sealed class PlayerItemState
        {
            /// <summary>依 <see cref="ItemKind"/> 索引：這種券在比賽時間幾秒之後才能再買。</summary>
            public readonly double[] ReadyAt = new double[ItemKindCount];

            public int Uses;
        }

        public ItemShop(ItemConfig config)
        {
            _config = config;
        }

        /// <summary>本場（或上一場，直到下一場開跑前）所有成功使用的券，依使用順序排列。</summary>
        public IReadOnlyList<ItemUse> Uses
        {
            get { return _uses; }
        }

        /// <summary>新的一場開跑：清掉所有人的冷卻、張數與使用紀錄。比賽時間會從 0 重新起算。</summary>
        public void BeginRace()
        {
            _players.Clear();
            _uses.Clear();
        }

        /// <summary>
        /// 嘗試購買並使用一張券。成功時扣款、套用效果、開始冷卻並記錄。
        /// </summary>
        public ItemRejection TryUse(PlayerAccount account, ItemKind kind, int lane, RaceEngine race)
        {
            ItemRejection common = CheckCommon(account, kind, lane, race);
            if (common != ItemRejection.None)
            {
                return common;
            }

            ItemRejection specific = kind == ItemKind.Obstacle
                ? CheckObstacle(race, lane)
                : CheckSpeedEffect(race, lane);
            if (specific != ItemRejection.None)
            {
                return specific;
            }

            if (account.Balance < _config.CostOf(kind))
            {
                return ItemRejection.InsufficientChips;
            }

            bool applied = kind == ItemKind.Obstacle
                ? PlaceObstacle(race, lane)
                : ApplySpeedEffect(account, kind, lane, race);
            if (!applied)
            {
                return ItemRejection.HorseFinished;
            }

            Charge(account, kind, lane, race.ElapsedSeconds);
            return ItemRejection.None;
        }

        /// <summary>這名玩家的這種券還要冷卻幾秒；可以買時為 0。</summary>
        public double CooldownRemaining(string playerId, ItemKind kind, double raceTime)
        {
            PlayerItemState state;
            if (string.IsNullOrEmpty(playerId) || !_players.TryGetValue(playerId, out state))
            {
                return 0.0;
            }

            double remaining = state.ReadyAt[(int)kind] - raceTime;
            return remaining > 0.0 ? remaining : 0.0;
        }

        /// <summary>
        /// 把使用紀錄彙整成「閘號 × 種類 × 玩家 → 張數」，依閘號、種類排列，同組內用得多的在前。
        /// </summary>
        public List<ItemUsageLine> Summarize()
        {
            List<ItemUsageLine> lines = new List<ItemUsageLine>();
            Dictionary<string, ItemUsageLine> byKey = new Dictionary<string, ItemUsageLine>();

            foreach (ItemUse use in _uses)
            {
                string key = use.Lane + "|" + (int)use.Kind + "|" + use.PlayerId;
                ItemUsageLine line;
                if (!byKey.TryGetValue(key, out line))
                {
                    line = new ItemUsageLine
                    {
                        Lane = use.Lane, Kind = use.Kind, PlayerId = use.PlayerId, Nickname = use.Nickname
                    };
                    byKey[key] = line;
                    lines.Add(line);
                }

                line.Count++;
            }

            // 穩定排序：同張數時維持第一次使用的先後
            List<ItemUsageLine> ordered = new List<ItemUsageLine>(lines);
            ordered.Sort((a, b) =>
            {
                int byLane = a.Lane.CompareTo(b.Lane);
                if (byLane != 0)
                {
                    return byLane;
                }

                int byKind = ((int)a.Kind).CompareTo((int)b.Kind);
                if (byKind != 0)
                {
                    return byKind;
                }

                int byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : lines.IndexOf(a).CompareTo(lines.IndexOf(b));
            });
            return ordered;
        }

        // ---- 檢查 ----

        private ItemRejection CheckCommon(PlayerAccount account, ItemKind kind, int lane, RaceEngine race)
        {
            if (race == null)
            {
                return ItemRejection.NotRacing;
            }

            if (account == null)
            {
                return ItemRejection.UnknownPlayer;
            }

            if (lane < 0 || lane >= race.HorseCount)
            {
                return ItemRejection.InvalidLane;
            }

            if (race.Horses[lane].Finished)
            {
                return ItemRejection.HorseFinished;
            }

            PlayerItemState state = StateOf(account.PlayerId);
            if (race.ElapsedSeconds < state.ReadyAt[(int)kind])
            {
                return ItemRejection.CoolingDown;
            }

            if (_config.UsesPerRace > 0 && state.Uses >= _config.UsesPerRace)
            {
                return ItemRejection.NoUsesLeft;
            }

            return ItemRejection.None;
        }

        private ItemRejection CheckSpeedEffect(RaceEngine race, int lane)
        {
            return race.ActiveEffectCount(lane) >= _config.MaxStacksPerHorse
                ? ItemRejection.HorseEffectsFull
                : ItemRejection.None;
        }

        private ItemRejection CheckObstacle(RaceEngine race, int lane)
        {
            HorseState horse = race.Horses[lane];

            if (horse.ObstacleAt != HorseState.NoObstacle)
            {
                return ItemRejection.ObstacleAlreadyPlaced;
            }

            bool recovering = horse.StunRemaining > 0.0
                              || race.ElapsedSeconds - horse.StunEndedAt < _config.ObstacleImmunitySeconds;
            if (recovering)
            {
                return ItemRejection.HorseRecovering;
            }

            return ObstaclePositionFor(horse) >= race.TrackLengthMeters - FinishGuardMeters
                ? ItemRejection.TooCloseToFinish
                : ItemRejection.None;
        }

        // ---- 套用 ----

        private bool ApplySpeedEffect(PlayerAccount account, ItemKind kind, int lane, RaceEngine race)
        {
            EffectKind effectKind = kind == ItemKind.Boost ? EffectKind.Boost : EffectKind.Slow;
            SpeedEffect effect = new SpeedEffect
            {
                Kind = effectKind,
                Multiplier = _config.MultiplierFor(effectKind),
                RemainingSeconds = _config.DurationSeconds,
                SourcePlayerId = account.PlayerId,
                SourceNickname = account.Nickname
            };

            return race.ApplyEffect(lane, effect);
        }

        private bool PlaceObstacle(RaceEngine race, int lane)
        {
            HorseState horse = race.Horses[lane];
            return race.PlaceObstacle(lane, ObstaclePositionFor(horse), _config.ObstacleStunSeconds);
        }

        private double ObstaclePositionFor(HorseState horse)
        {
            return horse.Distance + _config.ObstacleLeadMeters;
        }

        private void Charge(PlayerAccount account, ItemKind kind, int lane, double now)
        {
            PlayerItemState state = StateOf(account.PlayerId);
            account.Balance -= _config.CostOf(kind);
            state.Uses++;
            state.ReadyAt[(int)kind] = now + _config.CooldownOf(kind);

            _uses.Add(new ItemUse
            {
                Lane = lane, Kind = kind, PlayerId = account.PlayerId, Nickname = account.Nickname
            });
        }

        private PlayerItemState StateOf(string playerId)
        {
            string key = playerId ?? "";
            PlayerItemState state;
            if (!_players.TryGetValue(key, out state))
            {
                state = new PlayerItemState();
                _players[key] = state;
            }

            return state;
        }
    }
}
