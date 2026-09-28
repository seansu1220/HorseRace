using System.Collections.Generic;
using System.Text;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 遊戲時間到、最後一場結束後的全螢幕「最終排名」。只負責顯示，資料由 <see cref="RaceHud"/> 轉交。
    /// 名次、玩家名、籌碼各自一欄，每欄各自對齊。
    /// </summary>
    public sealed class FinalScreen
    {
        /// <summary>最終排名顯示幾名。</summary>
        public const int RankCount = 10;

        private static readonly Color OverlayColor = new Color(0.03f, 0.06f, 0.045f, 0.94f);

        private RectTransform _root;
        private Text _ranks;
        private Text _names;
        private Text _chips;

        public static FinalScreen Build(Transform canvas)
        {
            FinalScreen screen = new FinalScreen();
            screen._root = UiFactory.Panel(canvas, "Final", OverlayColor);
            UiFactory.Stretch(screen._root, 0f, 0f, 0f, 0f);

            Text title = UiFactory.Label(screen._root, "Title", "遊戲結束", 96,
                TextAnchor.MiddleCenter, UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(1200f, 120f));

            Text subtitle = UiFactory.Label(screen._root, "Subtitle", "最終排名", 40,
                TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place((RectTransform)subtitle.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -190f), new Vector2(1200f, 56f));

            screen._ranks = screen.Column("Ranks", -450f, 100f, TextAnchor.UpperRight);
            screen._names = screen.Column("Names", -320f, 520f, TextAnchor.UpperLeft);
            screen._chips = screen.Column("Chips", 230f, 220f, TextAnchor.UpperRight);

            Text hint = UiFactory.Label(screen._root, "Hint", "主持人：按 R 重新開始", 28,
                TextAnchor.MiddleCenter, UiFactory.MutedTextColor);
            UiFactory.Place((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(1200f, 44f));

            screen._root.gameObject.SetActive(false);
            return screen;
        }

        public void Show(IReadOnlyList<PlayerAccount> ranked)
        {
            StringBuilder ranks = new StringBuilder();
            StringBuilder names = new StringBuilder();
            StringBuilder chips = new StringBuilder();

            for (int i = 0; ranked != null && i < ranked.Count && i < RankCount; i++)
            {
                string open = i == 0 ? "<color=#E4B64A>" : "";
                string close = i == 0 ? "</color>" : "";
                ranks.Append(open).Append(i + 1).Append('.').Append(close).Append('\n');
                names.Append(open).Append(ranked[i].Nickname).Append(close).Append('\n');
                chips.Append(open).Append(ranked[i].Balance).Append(close).Append('\n');
            }

            if (ranks.Length == 0)
            {
                names.Append("沒有玩家");
            }

            _ranks.text = ranks.ToString();
            _names.text = names.ToString();
            _chips.text = chips.ToString();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }

        /// <summary>一欄名單：三欄用同樣的字級與行距，逐行自然對齊。</summary>
        private Text Column(string name, float x, float width, TextAnchor anchor)
        {
            Text column = UiFactory.Label(_root, name, "", 44, anchor, UiFactory.TextColor, FontStyle.Bold);
            column.lineSpacing = 1.15f;
            UiFactory.Place((RectTransform)column.transform, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -270f), new Vector2(width, 700f));
            return column;
        }
    }
}
