using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>加入啦啦隊被拒絕的原因。措辭留給呈現層決定。</summary>
    public enum CheerRejection
    {
        None = 0,

        /// <summary>只有下注階段可以加入。</summary>
        NotBetting = 1,

        UnknownPlayer = 2,

        /// <summary>這一場已經加入過了。</summary>
        AlreadyJoined = 3,

        InsufficientChips = 4
    }

    /// <summary>
    /// 啦啦隊：下注階段花籌碼加入，比賽中才能搖手機幫馬加油。每一場都要重新加入。
    /// 同時記錄本場每個人實際計入的出力步數，賽後頒「最強啦啦隊」用。
    /// </summary>
    public sealed class CheerSquad
    {
        private readonly ItemConfig _config;
        private readonly HashSet<string> _members = new HashSet<string>();
        private readonly Dictionary<string, int> _steps = new Dictionary<string, int>();

        /// <summary>第一次出力的先後，平手時先出力的人得獎。</summary>
        private readonly List<string> _stepOrder = new List<string>();

        public CheerSquad(ItemConfig config)
        {
            _config = config;
        }

        /// <summary>本場有出力的玩家，依第一次出力的先後排列。</summary>
        public IReadOnlyList<string> Contributors
        {
            get { return _stepOrder; }
        }

        public int MemberCount
        {
            get { return _members.Count; }
        }

        /// <summary>新的一場開放下注：上一場的啦啦隊資格作廢。</summary>
        public void BeginBetting()
        {
            _members.Clear();
        }

        /// <summary>開跑：出力紀錄從零開始（上一場的留到這之前，賽後頒獎要用）。</summary>
        public void BeginRace()
        {
            _steps.Clear();
            _stepOrder.Clear();
        }

        /// <summary>加入啦啦隊並扣款。階段檢查由 <see cref="GameLoop"/> 負責。</summary>
        public CheerRejection TryJoin(PlayerAccount account)
        {
            if (account == null)
            {
                return CheerRejection.UnknownPlayer;
            }

            if (_members.Contains(account.PlayerId))
            {
                return CheerRejection.AlreadyJoined;
            }

            if (account.Balance < _config.CheerCost)
            {
                return CheerRejection.InsufficientChips;
            }

            account.Balance -= _config.CheerCost;
            _members.Add(account.PlayerId);
            return CheerRejection.None;
        }

        public bool IsMember(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && _members.Contains(playerId);
        }

        /// <summary>記下實際計入賽事的步數（已經過每人上限過濾）。</summary>
        public void RecordSteps(string playerId, int steps)
        {
            if (steps <= 0 || string.IsNullOrEmpty(playerId))
            {
                return;
            }

            int total;
            if (!_steps.TryGetValue(playerId, out total))
            {
                _stepOrder.Add(playerId);
            }

            _steps[playerId] = total + steps;
        }

        public int StepsOf(string playerId)
        {
            int total;
            return playerId != null && _steps.TryGetValue(playerId, out total) ? total : 0;
        }
    }
}
