using System.Collections.Generic;

namespace HorseRace.Core
{
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

        /// <summary>那匹馬身上的效果已經疊滿，避免全場圍剿同一匹。</summary>
        HorseEffectsFull = 7,

        /// <summary>籌碼不夠。</summary>
        InsufficientChips = 8
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
        private readonly ItemConfig _config;
        private readonly Dictionary<string, PlayerItemState> _players = new Dictionary<string, PlayerItemState>();

        /// <summary>一名玩家本場的道具使用狀態。</summary>
        private sealed class PlayerItemState
        {
            /// <summary>依 <see cref="EffectKind"/> 索引：這種券在比賽時間幾秒之後才能再買。</summary>
            public readonly double[] ReadyAt = new double[EffectKindCount];

            public int Uses;
        }

        /// <summary><see cref="EffectKind"/> 的種類數。</summary>
        private const int EffectKindCount = 2;

        public ItemShop(ItemConfig config)
        {
            _config = config;
        }

        /// <summary>新的一場開跑：清掉所有人的冷卻與張數。比賽時間會從 0 重新起算，舊紀錄沒有意義。</summary>
        public void BeginRace()
        {
            _players.Clear();
        }

        /// <summary>
        /// 嘗試購買並使用一張券。成功時扣款、套用效果並開始冷卻。
        /// </summary>
        public ItemRejection TryUse(PlayerAccount account, EffectKind kind, int lane, RaceEngine race)
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
            double now = race.ElapsedSeconds;

            if (now < state.ReadyAt[(int)kind])
            {
                return ItemRejection.CoolingDown;
            }

            if (_config.UsesPerRace > 0 && state.Uses >= _config.UsesPerRace)
            {
                return ItemRejection.NoUsesLeft;
            }

            if (race.ActiveEffectCount(lane) >= _config.MaxStacksPerHorse)
            {
                return ItemRejection.HorseEffectsFull;
            }

            if (account.Balance < _config.Cost)
            {
                return ItemRejection.InsufficientChips;
            }

            return Apply(account, kind, lane, race, state, now);
        }

        /// <summary>這名玩家的這種券還要冷卻幾秒；可以買時為 0。</summary>
        public double CooldownRemaining(string playerId, EffectKind kind, double raceTime)
        {
            PlayerItemState state;
            if (string.IsNullOrEmpty(playerId) || !_players.TryGetValue(playerId, out state))
            {
                return 0.0;
            }

            double remaining = state.ReadyAt[(int)kind] - raceTime;
            return remaining > 0.0 ? remaining : 0.0;
        }

        private ItemRejection Apply(
            PlayerAccount account, EffectKind kind, int lane, RaceEngine race, PlayerItemState state, double now)
        {
            SpeedEffect effect = new SpeedEffect
            {
                Kind = kind,
                Multiplier = _config.MultiplierFor(kind),
                RemainingSeconds = _config.DurationSeconds,
                SourcePlayerId = account.PlayerId,
                SourceNickname = account.Nickname
            };

            if (!race.ApplyEffect(lane, effect))
            {
                return ItemRejection.HorseFinished;
            }

            account.Balance -= _config.Cost;
            state.Uses++;
            state.ReadyAt[(int)kind] = now + _config.EffectiveCooldownSeconds;
            return ItemRejection.None;
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
