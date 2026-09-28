using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 結算與獎項畫面用的美術素材，從 Resources/Art/Results/ 載入（第一次用到時載入一次）。
    ///
    /// 換素材只要用同樣的檔名覆蓋檔案，不必改程式。任何一張找不到都回傳 null，
    /// 呼叫端要退回純色面板或文字——現場活動不能因為少一張圖就開不起來。
    /// 素材來源與授權見 docs/THIRD_PARTY_NOTICES.md。
    /// </summary>
    public static class ResultArt
    {
        private const string Folder = "Art/Results/";

        /// <summary>panel_frame.png 的九宮格邊框寬度（原圖像素），邊框不隨面板大小被拉伸。</summary>
        private const float PanelBorderPixels = 96f;

        /// <summary>confetti.png 是 4×4 格的彩帶圖集。</summary>
        private const int ConfettiGrid = 4;

        private static bool _loaded;
        private static Texture2D _background;
        private static Sprite _panel;
        private static Sprite _ribbon;
        private static Sprite[] _medals;
        private static Sprite[] _confetti;
        private static readonly Dictionary<AwardKind, Sprite> AwardIcons = new Dictionary<AwardKind, Sprite>();

        /// <summary>全螢幕背景（夜間賽馬場）。</summary>
        public static Texture2D Background
        {
            get { EnsureLoaded(); return _background; }
        }

        /// <summary>九宮格面板（金框深綠底）。</summary>
        public static Sprite Panel
        {
            get { EnsureLoaded(); return _panel; }
        }

        /// <summary>金色緞帶標題。</summary>
        public static Sprite Ribbon
        {
            get { EnsureLoaded(); return _ribbon; }
        }

        /// <summary>第 1～3 名的獎牌；其他名次回傳 null。</summary>
        public static Sprite Medal(int position)
        {
            EnsureLoaded();
            return position >= 0 && position < _medals.Length ? _medals[position] : null;
        }

        public static Sprite AwardIcon(AwardKind kind)
        {
            EnsureLoaded();
            Sprite icon;
            return AwardIcons.TryGetValue(kind, out icon) ? icon : null;
        }

        /// <summary>彩帶碎片；沒有素材時回傳空陣列。</summary>
        public static Sprite[] Confetti
        {
            get { EnsureLoaded(); return _confetti; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _background = Load("result_bg");
            _panel = ToSprite(Load("panel_frame"), PanelBorderPixels);
            _ribbon = ToSprite(Load("ribbon"), 0f);

            _medals = new Sprite[3];
            for (int i = 0; i < _medals.Length; i++)
            {
                _medals[i] = ToSprite(Load("medal_" + (i + 1)), 0f);
            }

            AwardIcons[AwardKind.TicketTycoon] = ToSprite(Load("award_ticket_tycoon"), 0f);
            AwardIcons[AwardKind.TopCheerleader] = ToSprite(Load("award_top_cheerleader"), 0f);
            AwardIcons[AwardKind.Roadblocker] = ToSprite(Load("award_roadblocker"), 0f);
            AwardIcons[AwardKind.BigWinner] = ToSprite(Load("award_big_winner"), 0f);

            _confetti = SliceGrid(Load("confetti"), ConfettiGrid);
        }

        private static Texture2D Load(string name)
        {
            Texture2D texture = Resources.Load<Texture2D>(Folder + name);
            if (texture == null)
            {
                Debug.LogWarning("[ResultArt] 找不到素材 Resources/" + Folder + name + ".png，該處改用純色或文字。");
            }

            return texture;
        }

        private static Sprite ToSprite(Texture2D texture, float border)
        {
            if (texture == null)
            {
                return null;
            }

            Rect rect = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite[] SliceGrid(Texture2D texture, int grid)
        {
            if (texture == null)
            {
                return new Sprite[0];
            }

            float cellWidth = texture.width / (float)grid;
            float cellHeight = texture.height / (float)grid;
            Sprite[] pieces = new Sprite[grid * grid];
            for (int row = 0; row < grid; row++)
            {
                for (int column = 0; column < grid; column++)
                {
                    Rect rect = new Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
                    pieces[row * grid + column] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
                }
            }

            return pieces;
        }
    }
}
