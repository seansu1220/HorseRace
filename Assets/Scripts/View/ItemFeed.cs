using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 大螢幕右上角的即時播報：「阿明 → 蒼影 減速券 ×3」。
    ///
    /// 券便宜又能連買，全場一起按時每秒可能有十幾筆，所以同一個人對同一匹馬用同一種券時
    /// 合併成一列並累加次數；最多顯示幾列，新的在上面，舊的幾秒後淡出。
    /// </summary>
    public sealed class ItemFeed
    {
        private const int VisibleRows = 6;
        private const float RowHeight = 44f;
        private const float LifetimeSeconds = 5f;
        private const float FadeSeconds = 1f;

        private sealed class Entry
        {
            public string Nickname;
            public ItemKind Kind;
            public int Lane;
            public int Count;
            public float LastAt;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Text[] _rows;

        /// <summary>
        /// 每列的透明度用 CanvasGroup 控制：富文字的 &lt;color&gt; 標籤會蓋掉 Text 本身的 alpha，
        /// 只調 Text.color 的話有上色的字不會跟著淡出。
        /// </summary>
        private CanvasGroup[] _fades;

        /// <summary>在指定畫布下建立，位置在右上角狀態列下方。</summary>
        public static ItemFeed Build(Transform canvas)
        {
            ItemFeed feed = new ItemFeed();
            RectTransform root = UiFactory.Node(canvas, "ItemFeed");
            UiFactory.Place(root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-40f, -110f), new Vector2(620f, VisibleRows * RowHeight));

            feed._rows = new Text[VisibleRows];
            feed._fades = new CanvasGroup[VisibleRows];
            for (int i = 0; i < VisibleRows; i++)
            {
                Text row = UiFactory.Label(root, "Row_" + i, "", 28, TextAnchor.MiddleRight,
                    UiFactory.TextColor, FontStyle.Bold);
                UiFactory.Place((RectTransform)row.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -i * RowHeight), new Vector2(620f, RowHeight));

                // 播報疊在 3D 賽道上，描邊才看得清楚
                Outline outline = row.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(2f, -2f);
                feed._rows[i] = row;
                feed._fades[i] = row.gameObject.AddComponent<CanvasGroup>();
            }

            return feed;
        }

        /// <summary>記一筆使用。短時間內重複的會合併並移到最上面。</summary>
        public void Push(string nickname, ItemKind kind, int lane, float now)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry existing = _entries[i];
                if (existing.Nickname == nickname && existing.Kind == kind && existing.Lane == lane)
                {
                    existing.Count++;
                    existing.LastAt = now;
                    _entries.RemoveAt(i);
                    _entries.Insert(0, existing);
                    return;
                }
            }

            _entries.Insert(0, new Entry { Nickname = nickname, Kind = kind, Lane = lane, Count = 1, LastAt = now });
            if (_entries.Count > VisibleRows)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
        }

        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>每幀更新顯示：過期的移除，快過期的淡出。</summary>
        public void Refresh(float now, HorseConfig[] lineup)
        {
            _entries.RemoveAll(entry => now - entry.LastAt > LifetimeSeconds);

            for (int i = 0; i < _rows.Length; i++)
            {
                Text row = _rows[i];
                if (i >= _entries.Count)
                {
                    row.text = "";
                    continue;
                }

                Entry entry = _entries[i];
                row.text = Describe(entry, lineup);

                float remaining = LifetimeSeconds - (now - entry.LastAt);
                _fades[i].alpha = Mathf.Clamp01(remaining / FadeSeconds);
            }
        }

        private static string Describe(Entry entry, HorseConfig[] lineup)
        {
            bool known = lineup != null && entry.Lane >= 0 && entry.Lane < lineup.Length;
            string horseName = known ? lineup[entry.Lane].Name : "第 " + (entry.Lane + 1) + " 號";
            string horseHex = known ? lineup[entry.Lane].ColorHex : "#FFFFFF";

            string text = entry.Nickname + "  →  " + ItemStyle.Tint(horseName, horseHex) + "  "
                          + ItemStyle.Tint(ItemStyle.Name(entry.Kind), ItemStyle.Hex(entry.Kind));
            return entry.Count > 1 ? text + "  ×" + entry.Count : text;
        }
    }
}
