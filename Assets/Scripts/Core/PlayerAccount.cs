using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>單一玩家的一筆注。</summary>
    public sealed class Bet
    {
        public int Lane;
        public int Amount;
    }

    /// <summary>
    /// 一名玩家的帳戶。純娛樂籌碼，只存在記憶體，程式關掉就沒了——
    /// 不落地、不涉及真實金流，因此沒有帳號系統也沒有資料庫。
    /// </summary>
    public sealed class PlayerAccount
    {
        /// <summary>玩家識別碼，由手機端存在 localStorage，重整或斷線回來能認回同一個人。</summary>
        public string PlayerId;

        public string Nickname;

        /// <summary>目前籌碼。下注時立即扣除，所以這個數字永遠是「還能押的錢」。</summary>
        public int Balance;

        /// <summary>本場的注單。換場時清空。</summary>
        public readonly List<Bet> Bets = new List<Bet>();

        /// <summary>上一場的派彩金額（含本金）。</summary>
        public int LastPayout;

        /// <summary>上一場的淨輸贏，供手機直接顯示「+240」或「-100」。</summary>
        public int LastDelta;

        /// <summary>本場押在所有馬身上的總金額。</summary>
        public int TotalStaked
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Bets.Count; i++)
                {
                    total += Bets[i].Amount;
                }

                return total;
            }
        }

        /// <summary>本場押在指定閘號上的金額。</summary>
        public int StakeOn(int lane)
        {
            int total = 0;
            for (int i = 0; i < Bets.Count; i++)
            {
                if (Bets[i].Lane == lane)
                {
                    total += Bets[i].Amount;
                }
            }

            return total;
        }
    }
}
