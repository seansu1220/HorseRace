using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>賽後獎項。顯示的名稱由呈現層決定。</summary>
    public enum AwardKind
    {
        /// <summary>券券富翁：本場買最多張券的人。</summary>
        TicketTycoon = 0,

        /// <summary>最強啦啦隊：本場出力步數最多的人。</summary>
        TopCheerleader = 1,

        /// <summary>路霸：本場放最多障礙物的人。</summary>
        Roadblocker = 2,

        /// <summary>本場大贏家：這一場下注賺最多的人。</summary>
        BigWinner = 3
    }

    /// <summary>一個獎項的得主與成績（張數、步數或贏得的籌碼）。</summary>
    public sealed class Award
    {
        public AwardKind Kind;
        public string PlayerId;
        public string Nickname;
        public int Value;
    }

    /// <summary>
    /// 賽後頒獎。純函式：只讀本場的紀錄，不改任何狀態。
    /// 沒有人符合的獎項（例如沒人放障礙物）就不頒；平手時先做到的人得獎。
    /// </summary>
    public static class RaceAwards
    {
        public static List<Award> Compute(IReadOnlyList<ItemUse> uses, CheerSquad cheer, BettingBook book)
        {
            List<Award> awards = new List<Award>();

            AddIfAny(awards, AwardKind.TicketTycoon, CountUses(uses, null), book);
            AddIfAny(awards, AwardKind.TopCheerleader, CheerTotals(cheer), book);
            AddIfAny(awards, AwardKind.Roadblocker, CountUses(uses, ItemKind.Obstacle), book);
            AddIfAny(awards, AwardKind.BigWinner, WinnerTotals(book), book);

            return awards;
        }

        /// <summary>每名玩家用了幾張券（指定種類，或 null 代表全部），依第一次使用的先後排列。</summary>
        private static List<KeyValuePair<string, int>> CountUses(IReadOnlyList<ItemUse> uses, ItemKind? onlyKind)
        {
            List<KeyValuePair<string, int>> totals = new List<KeyValuePair<string, int>>();
            foreach (ItemUse use in uses)
            {
                if (onlyKind.HasValue && use.Kind != onlyKind.Value)
                {
                    continue;
                }

                Increment(totals, use.PlayerId, 1);
            }

            return totals;
        }

        private static List<KeyValuePair<string, int>> CheerTotals(CheerSquad cheer)
        {
            List<KeyValuePair<string, int>> totals = new List<KeyValuePair<string, int>>();
            foreach (string playerId in cheer.Contributors)
            {
                totals.Add(new KeyValuePair<string, int>(playerId, cheer.StepsOf(playerId)));
            }

            return totals;
        }

        private static List<KeyValuePair<string, int>> WinnerTotals(BettingBook book)
        {
            List<KeyValuePair<string, int>> totals = new List<KeyValuePair<string, int>>();
            foreach (PlayerAccount account in book.Players)
            {
                totals.Add(new KeyValuePair<string, int>(account.PlayerId, account.LastDelta));
            }

            return totals;
        }

        private static void Increment(List<KeyValuePair<string, int>> totals, string playerId, int amount)
        {
            for (int i = 0; i < totals.Count; i++)
            {
                if (totals[i].Key == playerId)
                {
                    totals[i] = new KeyValuePair<string, int>(playerId, totals[i].Value + amount);
                    return;
                }
            }

            totals.Add(new KeyValuePair<string, int>(playerId, amount));
        }

        /// <summary>取最高分者（嚴格大於才換人，所以平手時先出現的得獎）；最高分不大於 0 就不頒。</summary>
        private static void AddIfAny(
            List<Award> awards, AwardKind kind, List<KeyValuePair<string, int>> totals, BettingBook book)
        {
            string bestId = null;
            int bestValue = 0;
            foreach (KeyValuePair<string, int> entry in totals)
            {
                if (entry.Value > bestValue)
                {
                    bestId = entry.Key;
                    bestValue = entry.Value;
                }
            }

            if (bestId == null)
            {
                return;
            }

            PlayerAccount account = book.Find(bestId);
            awards.Add(new Award
            {
                Kind = kind,
                PlayerId = bestId,
                Nickname = account != null ? account.Nickname : bestId,
                Value = bestValue
            });
        }
    }
}
