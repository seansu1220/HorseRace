using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>下注被拒絕的原因。用 enum 而非字串，顯示文字留給呈現層決定。</summary>
    public enum BetRejection
    {
        None = 0,

        /// <summary>不在下注階段。</summary>
        NotBettingPhase = 1,

        /// <summary>找不到這名玩家。</summary>
        UnknownPlayer = 2,

        /// <summary>閘號不存在。</summary>
        InvalidLane = 3,

        /// <summary>金額低於最低下注額。</summary>
        BelowMinimum = 4,

        /// <summary>籌碼不足。</summary>
        InsufficientChips = 5
    }

    /// <summary>一場結算的結果。</summary>
    public sealed class SettlementResult
    {
        public SettlementResult(int winnerLane, Dictionary<string, int> payoutByPlayer)
        {
            WinnerLane = winnerLane;
            PayoutByPlayer = payoutByPlayer;
        }

        public int WinnerLane { get; private set; }

        /// <summary>各玩家的派彩金額（含本金）。沒中的玩家不會出現在這裡。</summary>
        public Dictionary<string, int> PayoutByPlayer { get; private set; }
    }

    /// <summary>
    /// 籌碼與注單的帳本。純 C#，不知道 Unity 也不知道網路，
    /// 因此可以完整用單元測試驗證——這是整個遊戲唯一牽涉「輸贏」的地方，
    /// 出錯現場會吵起來，所以測試要寫滿。
    /// </summary>
    public sealed class BettingBook
    {
        private readonly RaceConfig _config;
        private readonly Dictionary<string, PlayerAccount> _players =
            new Dictionary<string, PlayerAccount>();

        /// <summary>維持加入順序，讓大螢幕的名單不會每幀跳動。</summary>
        private readonly List<PlayerAccount> _order = new List<PlayerAccount>();

        public BettingBook(RaceConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _config = config;
        }

        public int PlayerCount
        {
            get { return _order.Count; }
        }

        public IReadOnlyList<PlayerAccount> Players
        {
            get { return _order; }
        }

        public PlayerAccount Find(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            PlayerAccount account;
            return _players.TryGetValue(playerId, out account) ? account : null;
        }

        /// <summary>
        /// 加入或更新一名玩家。已存在的玩家只更新暱稱並保留籌碼，
        /// 這樣手機重整、息屏回來或換 Wi-Fi 都能回到原本的身分。
        /// </summary>
        public PlayerAccount Join(string playerId, string nickname)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            PlayerAccount account;
            if (_players.TryGetValue(playerId, out account))
            {
                if (!string.IsNullOrEmpty(nickname))
                {
                    account.Nickname = Sanitize(nickname);
                }

                return account;
            }

            account = new PlayerAccount
            {
                PlayerId = playerId,
                Nickname = Sanitize(nickname),
                Balance = _config.StartingChips
            };

            _players[playerId] = account;
            _order.Add(account);
            return account;
        }

        /// <summary>
        /// 下注。金額在此立即從籌碼扣除，所以帳戶餘額永遠等於「還能押的錢」，
        /// 不會出現同一筆錢押兩匹的競態。
        /// </summary>
        public BetRejection TryPlaceBet(string playerId, int lane, int amount, int horseCount)
        {
            PlayerAccount account = Find(playerId);
            if (account == null)
            {
                return BetRejection.UnknownPlayer;
            }

            if (lane < 0 || lane >= horseCount)
            {
                return BetRejection.InvalidLane;
            }

            if (amount < _config.MinimumBet)
            {
                return BetRejection.BelowMinimum;
            }

            if (amount > account.Balance)
            {
                return BetRejection.InsufficientChips;
            }

            account.Balance -= amount;

            // 押同一匹就併成一筆，手機端的注單才不會愈滾愈長
            for (int i = 0; i < account.Bets.Count; i++)
            {
                if (account.Bets[i].Lane == lane)
                {
                    account.Bets[i].Amount += amount;
                    return BetRejection.None;
                }
            }

            account.Bets.Add(new Bet { Lane = lane, Amount = amount });
            return BetRejection.None;
        }

        /// <summary>
        /// 每場開賽前呼叫：清空上一場的注單，並補發同情籌碼。
        ///
        /// 同情籌碼是刻意的設計。三十人的聚會裡，若有人第三場就輸光而只能乾坐著，
        /// 場子就冷掉了一角。輸光的人補到最低額度，讓他永遠有得玩。
        /// </summary>
        public void BeginRace()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                PlayerAccount account = _order[i];
                account.Bets.Clear();

                if (account.Balance < _config.CharityChips)
                {
                    account.Balance = _config.CharityChips;
                }
            }
        }

        /// <summary>
        /// 依冠軍閘號與賠率派彩。押中的玩家拿回「注額 × 賠率」（含本金），
        /// 沒押中的什麼都拿不到——本金在下注當下就已經扣掉了。
        /// </summary>
        public SettlementResult Settle(int winnerLane, double[] odds)
        {
            Dictionary<string, int> payouts = new Dictionary<string, int>();

            double winnerOdds = odds != null && winnerLane >= 0 && winnerLane < odds.Length
                ? odds[winnerLane]
                : 0.0;

            for (int i = 0; i < _order.Count; i++)
            {
                PlayerAccount account = _order[i];
                int staked = account.TotalStaked;
                int stakeOnWinner = account.StakeOn(winnerLane);

                int payout = stakeOnWinner > 0
                    ? (int)Math.Round(stakeOnWinner * winnerOdds, MidpointRounding.AwayFromZero)
                    : 0;

                account.Balance += payout;
                account.LastPayout = payout;
                account.LastDelta = payout - staked;

                if (payout > 0)
                {
                    payouts[account.PlayerId] = payout;
                }
            }

            return new SettlementResult(winnerLane, payouts);
        }

        /// <summary>籌碼排行榜，由高到低。並列時以加入順序決定，避免名次每幀跳動。</summary>
        public List<PlayerAccount> TopPlayers(int count)
        {
            List<PlayerAccount> ranked = new List<PlayerAccount>(_order);
            ranked.Sort(CompareByBalance);

            if (count > 0 && ranked.Count > count)
            {
                ranked.RemoveRange(count, ranked.Count - count);
            }

            return ranked;
        }

        private int CompareByBalance(PlayerAccount a, PlayerAccount b)
        {
            int byBalance = b.Balance.CompareTo(a.Balance);
            return byBalance != 0 ? byBalance : _order.IndexOf(a).CompareTo(_order.IndexOf(b));
        }

        /// <summary>
        /// 暱稱來自現場的手機，必須當成不可信的輸入處理：
        /// 去掉控制字元、限制長度，空的就給預設名字。
        /// </summary>
        private static string Sanitize(string nickname)
        {
            if (string.IsNullOrEmpty(nickname))
            {
                return "路人";
            }

            System.Text.StringBuilder cleaned = new System.Text.StringBuilder();
            for (int i = 0; i < nickname.Length && cleaned.Length < MaxNicknameLength; i++)
            {
                char character = nickname[i];
                if (!char.IsControl(character))
                {
                    cleaned.Append(character);
                }
            }

            string result = cleaned.ToString().Trim();
            return result.Length == 0 ? "路人" : result;
        }

        public const int MaxNicknameLength = 10;
    }
}
