using System.Collections.Generic;
using System.Text;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 開賽前的「掃碼入場」全螢幕畫面：大 QRCode、入場人數、最新加入的名字、主持人提示。
    /// 只負責顯示，資料一律由 <see cref="RaceHud"/> 轉交；QRCode 貼圖的生命週期也歸 RaceHud 管。
    /// </summary>
    public sealed class LobbyScreen
    {
        /// <summary>名字牆最多顯示幾個人。現場最愛看自己的名字出現在大螢幕上，但太多會擠成一團。</summary>
        private const int RecentNameCount = 10;

        /// <summary>右欄所有元素共用的左緣，名字再長也不會把標題推歪。</summary>
        private const float InfoColumnLeft = -20f;

        private static readonly Color OverlayColor = new Color(0.03f, 0.06f, 0.045f, 0.92f);
        private static readonly Color InkColor = new Color(0.17f, 0.15f, 0.10f);
        private static readonly Color ConnectedColor = new Color(0.42f, 0.85f, 0.52f);
        private static readonly Color ProblemColor = new Color(1f, 0.45f, 0.40f);

        private RectTransform _root;
        private RawImage _qrImage;
        private Text _qrPlaceholder;
        private Text _urlLabel;
        private Text _connectionLabel;
        private Text _countLabel;
        private Text _namesLabel;
        private int _shownPlayerCount = -1;

        /// <summary>在指定畫布下建立（預設隱藏）。請最後建立，才會蓋在其他介面之上。</summary>
        public static LobbyScreen Build(Transform canvas)
        {
            LobbyScreen screen = new LobbyScreen();
            screen._root = UiFactory.Panel(canvas, "Lobby", OverlayColor);
            UiFactory.Stretch(screen._root, 0f, 0f, 0f, 0f);

            screen.BuildQrColumn();
            screen.BuildInfoColumn();
            screen._root.gameObject.SetActive(false);
            return screen;
        }

        public void SetVisible(bool visible)
        {
            _root.gameObject.SetActive(visible);
        }

        /// <summary>貼圖為 null 代表網址還沒準備好，框內改顯示說明文字。</summary>
        public void SetJoinInfo(Texture2D qrCode, string caption)
        {
            _qrImage.texture = qrCode;
            _qrImage.enabled = qrCode != null;

            _qrPlaceholder.gameObject.SetActive(qrCode == null);
            _qrPlaceholder.text = string.IsNullOrEmpty(caption) ? "連線準備中…" : caption;
            _urlLabel.text = qrCode != null ? caption : "";
        }

        public void SetConnection(bool connected, string detail)
        {
            _connectionLabel.text = connected ? "大螢幕已連線" : detail;
            _connectionLabel.color = connected ? ConnectedColor : ProblemColor;
        }

        /// <summary>更新人數與名字牆。人數沒變就不重組字串（每幀都會被呼叫）。</summary>
        public void SetPlayers(int count, IReadOnlyList<PlayerAccount> players)
        {
            if (count == _shownPlayerCount)
            {
                return;
            }

            _shownPlayerCount = count;
            _countLabel.text = "<size=190><b>" + count + "</b></size>  人已入場";
            _namesLabel.text = count == 0 ? "還沒有人入場，第一個就是你" : "最新加入　" + JoinRecentNames(players);
        }

        private static string JoinRecentNames(IReadOnlyList<PlayerAccount> players)
        {
            StringBuilder names = new StringBuilder();
            int shown = 0;

            // 由新到舊，剛掃進來的人立刻看得到自己
            for (int i = players.Count - 1; i >= 0 && shown < RecentNameCount; i--)
            {
                string nickname = players[i].Nickname;
                if (string.IsNullOrEmpty(nickname))
                {
                    continue;
                }

                if (shown > 0)
                {
                    names.Append("、");
                }

                names.Append(nickname);
                shown++;
            }

            return names.ToString();
        }

        // ---- 版面 ----

        private void BuildQrColumn()
        {
            // 白底框本身就是 QRCode 的靜區，深色背景上沒有它手機掃不到
            RectTransform frame = UiFactory.Panel(_root, "QrFrame", Color.white);
            UiFactory.Place(frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400f, 30f), new Vector2(600f, 600f));

            RectTransform image = UiFactory.Node(frame, "QrImage");
            UiFactory.Stretch(image, 22f, 22f, 22f, 22f);
            _qrImage = image.gameObject.AddComponent<RawImage>();
            _qrImage.raycastTarget = false;
            _qrImage.enabled = false;

            _qrPlaceholder = UiFactory.Label(frame, "QrPlaceholder", "連線準備中…", 36,
                TextAnchor.MiddleCenter, InkColor, FontStyle.Bold);
            UiFactory.Stretch((RectTransform)_qrPlaceholder.transform, 40f, 40f, 40f, 40f);
            UiFactory.ShrinkToFit(_qrPlaceholder, 20);

            _urlLabel = UiFactory.Label(_root, "Url", "", 32, TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place((RectTransform)_urlLabel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400f, -310f), new Vector2(700f, 48f));
            UiFactory.ShrinkToFit(_urlLabel, 16);

            _connectionLabel = UiFactory.Label(_root, "Connection", "", 24, TextAnchor.MiddleCenter,
                UiFactory.MutedTextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_connectionLabel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-400f, -358f), new Vector2(700f, 36f));
            UiFactory.ShrinkToFit(_connectionLabel, 14);
        }

        private void BuildInfoColumn()
        {
            AddLabel("Eyebrow", "今晚的賽馬場", 30, UiFactory.MutedTextColor, FontStyle.Bold, 300f, 44f);
            AddLabel("Title", "掃碼入場", 104, UiFactory.AccentColor, FontStyle.Bold, 215f, 130f);
            AddLabel("Subtitle", "打開手機相機，對準左邊的 QRCode", 34, UiFactory.TextColor, FontStyle.Normal, 118f, 50f);

            _countLabel = AddLabel("Count", "", 46, UiFactory.TextColor, FontStyle.Normal, -20f, 220f);
            _namesLabel = AddLabel("Names", "", 32, UiFactory.MutedTextColor, FontStyle.Normal, -190f, 110f);
            _namesLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _namesLabel.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform hintBar = UiFactory.Panel(_root, "HostHint", new Color(1f, 1f, 1f, 0.06f));
            UiFactory.Place(hintBar, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(InfoColumnLeft, -325f), new Vector2(760f, 76f));

            Text hint = UiFactory.Label(hintBar, "Text", "主持人：人到齊後按 空白鍵 開始第一場", 34,
                TextAnchor.MiddleLeft, UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Stretch((RectTransform)hint.transform, 28f, 0f, 20f, 0f);
            UiFactory.ShrinkToFit(hint, 18);
        }

        private Text AddLabel(string name, string content, int fontSize, Color color, FontStyle style,
                              float y, float height)
        {
            Text label = UiFactory.Label(_root, name, content, fontSize, TextAnchor.MiddleLeft, color, style);
            UiFactory.Place((RectTransform)label.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(InfoColumnLeft, y), new Vector2(760f, height));
            return label;
        }
    }
}
