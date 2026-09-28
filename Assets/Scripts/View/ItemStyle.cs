using System.Collections.Generic;
using System.Text;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 道具券在大螢幕上的名稱與顏色，以及賽後使用統計的文字格式。
    /// 顏色與手機頁（style.css 的 --boost／--slow／--obstacle）一致，大螢幕和手機看起來是同一套。
    /// </summary>
    public static class ItemStyle
    {
        private const string BoostHex = "#FF8A3D";
        private const string SlowHex = "#72B9FF";
        private const string ObstacleHex = "#E8577E";

        /// <summary>同一種效果最多逐一畫幾個圖示，再多就改成「▲×10」，免得一整排擠爆。</summary>
        private const int MaxRepeatedIcons = 3;

        /// <summary>
        /// 一匹馬身上的狀態圖示：▲ 加速、▼ 減速、暈 被絆住、■ 前方有障礙物。
        /// 有幾個就畫幾個；超過 <see cref="MaxRepeatedIcons"/> 個就寫成「▲×10」。
        /// </summary>
        public static string EffectBadges(int boosts, int slows, bool stunned, int obstaclesAhead)
        {
            StringBuilder text = new StringBuilder();
            AppendBadge(text, "▲", boosts, BoostHex);
            AppendBadge(text, "▼", slows, SlowHex);
            AppendBadge(text, "暈", stunned ? 1 : 0, ObstacleHex);
            AppendBadge(text, "■", obstaclesAhead, ObstacleHex);
            return text.ToString();
        }

        private static void AppendBadge(StringBuilder text, string icon, int count, string hex)
        {
            if (count <= 0)
            {
                return;
            }

            if (text.Length > 0)
            {
                text.Append(' ');
            }

            string body = count <= MaxRepeatedIcons ? Repeat(icon, count) : icon + "×" + count;
            text.Append(Tint(body, hex));
        }

        private static string Repeat(string icon, int count)
        {
            StringBuilder repeated = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                repeated.Append(icon);
            }

            return repeated.ToString();
        }

        /// <summary>賽後統計每種券最多列幾個人，其餘合併成「等 N 人」，免得一行擠爆。</summary>
        private const int NamesPerKind = 4;

        public static string Name(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Slow:
                    return "減速券";
                case ItemKind.Obstacle:
                    return "障礙券";
                default:
                    return "加速券";
            }
        }

        public static string Hex(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Slow:
                    return SlowHex;
                case ItemKind.Obstacle:
                    return ObstacleHex;
                default:
                    return BoostHex;
            }
        }

        public static Color ColorOf(ItemKind kind)
        {
            return MaterialLibrary.ParseHex(Hex(kind), Color.white);
        }

        /// <summary>包上 uGUI 富文字顏色標籤。</summary>
        public static string Tint(string text, string hex)
        {
            return "<color=" + hex + ">" + text + "</color>";
        }

        /// <summary>
        /// 某匹馬本場被用了哪些券：「加速 阿明×2、小美×1　減速 老王×2」。沒人用過時回傳說明文字。
        /// </summary>
        public static string FormatUsage(IReadOnlyList<ItemUsageLine> lines, int lane)
        {
            StringBuilder text = new StringBuilder();

            foreach (ItemKind kind in new[] { ItemKind.Boost, ItemKind.Slow, ItemKind.Obstacle })
            {
                List<ItemUsageLine> ofKind = new List<ItemUsageLine>();
                foreach (ItemUsageLine line in lines)
                {
                    if (line.Lane == lane && line.Kind == kind)
                    {
                        ofKind.Add(line);
                    }
                }

                if (ofKind.Count == 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append("　　");
                }

                text.Append(Tint(Name(kind).Substring(0, 2), Hex(kind))).Append(' ');
                AppendNames(text, ofKind);
            }

            return text.Length > 0 ? text.ToString() : "沒有人對牠用券";
        }

        private static void AppendNames(StringBuilder text, List<ItemUsageLine> ofKind)
        {
            int shown = Mathf.Min(NamesPerKind, ofKind.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    text.Append('、');
                }

                text.Append(ofKind[i].Nickname).Append('×').Append(ofKind[i].Count);
            }

            if (ofKind.Count > shown)
            {
                text.Append(" 等 ").Append(ofKind.Count).Append(" 人");
            }
        }
    }
}
