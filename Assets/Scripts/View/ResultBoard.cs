using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 大螢幕的名次與結算畫面。
    ///
    /// 衝線特寫（Photo）時只顯示名次面板，疊在 3D 終點畫面上；
    /// 結算（Settle）時換上全螢幕的夜間賽馬場背景、飄落的彩帶，獎項卡片依序彈出。
    /// 前三名用金銀銅獎牌，名次一列一列滑入。
    ///
    /// 美術素材由 <see cref="ResultArt"/> 提供；任何一張缺少時退回純色面板或文字，版面不變。
    /// </summary>
    public sealed class ResultBoard : MonoBehaviour
    {
        private const float BoardWidth = 1180f;
        private const float RowHeight = 86f;
        private const float HeaderHeight = 176f;
        private const float AwardsAreaHeight = 300f;
        private const float BottomPadding = 34f;
        private const float BoardCenterY = -40f;

        private const float CardWidth = 250f;
        private const float CardHeight = 232f;
        private const float CardSpacing = 22f;
        private const int MaxAwardCards = 4;

        private const float RowRevealDelay = 0.12f;
        private const float RowRevealSeconds = 0.35f;
        private const float CardRevealStart = 0.35f;
        private const float CardRevealDelay = 0.2f;
        private const float CardRevealSeconds = 0.45f;
        private const float BackdropFadeSeconds = 0.5f;

        private const int ConfettiCount = 70;

        private static readonly Color RibbonInk = new Color(0.23f, 0.16f, 0.05f);
        private static readonly Color CardColor = new Color(0.89f, 0.71f, 0.29f, 0.13f);
        private static readonly Color FallbackBackdrop = new Color(0.03f, 0.06f, 0.045f, 0.96f);

        private sealed class Row
        {
            public RectTransform Root;
            public CanvasGroup Fade;
            public Image Medal;
            public Text Position;
            public Image Chip;
            public Text Name;
            public Text Time;
            public Text Usage;
        }

        private sealed class Card
        {
            public RectTransform Root;
            public CanvasGroup Fade;
            public Image Icon;
            public Text Title;
            public Text Name;
            public Text Detail;
        }

        private RectTransform _root;
        private RawImage _backdrop;
        private Image _backdropFallback;
        private RectTransform _board;
        private Text _title;
        private Text _subtitle;
        private Row[] _rows;
        private Card[] _cards;
        private int _cardCount;
        private ConfettiRain _confetti;

        private bool _settleMode;
        private float _rowsShownAt;
        private float _cardsShownAt;
        private int _rowCount;

        public static ResultBoard Build(Transform canvas, int laneCount)
        {
            RectTransform root = UiFactory.Node(canvas, "ResultBoard");
            UiFactory.Stretch(root, 0f, 0f, 0f, 0f);

            ResultBoard board = root.gameObject.AddComponent<ResultBoard>();
            board._root = root;
            board.BuildBackdrop();
            board._confetti = ConfettiRain.Build(root, ConfettiCount);
            board.BuildBoard(laneCount);
            root.gameObject.SetActive(false);
            return board;
        }

        /// <summary>衝線時揭曉名次（只有名次面板，看得到後面的終點畫面）。</summary>
        public void ShowPlacings(RaceEngine race, int[] order, HorseConfig[] lineup, IReadOnlyList<ItemUsageLine> usage)
        {
            _root.gameObject.SetActive(true);
            _settleMode = false;
            _cardCount = 0;
            _title.text = "名　次";
            _subtitle.text = "";
            _rowCount = order == null ? 0 : Mathf.Min(order.Length, _rows.Length);

            for (int position = 0; position < _rows.Length; position++)
            {
                bool active = position < _rowCount;
                _rows[position].Root.gameObject.SetActive(active);
                if (active)
                {
                    FillRow(_rows[position], position, order[position], race, lineup, usage);
                }
            }

            foreach (Card card in _cards)
            {
                card.Root.gameObject.SetActive(false);
            }

            _rowsShownAt = Time.unscaledTime;
            SetBackdropVisible(false);
            _confetti.Stop();
            ResizeBoard();
        }

        /// <summary>結算：換上背景與彩帶，獎項卡片依序彈出。沒有獎項時只顯示名次。</summary>
        public void ShowAwards(IReadOnlyList<Award> awards)
        {
            _root.gameObject.SetActive(true);
            _settleMode = true;
            _title.text = "本 場 結 果";
            _cardCount = awards == null ? 0 : Mathf.Min(awards.Count, MaxAwardCards);

            for (int i = 0; i < _cards.Length; i++)
            {
                bool active = i < _cardCount;
                _cards[i].Root.gameObject.SetActive(active);
                if (active)
                {
                    FillCard(_cards[i], awards[i]);
                }
            }

            LayoutCards();
            _cardsShownAt = Time.unscaledTime;
            SetBackdropVisible(true);
            _confetti.Play();
            ResizeBoard();
        }

        /// <summary>結算時標題下方的「下一場 N 秒後開始」（頂部的倒數被背景蓋住了）。</summary>
        public void SetCountdown(double seconds)
        {
            _subtitle.text = _settleMode && seconds > 0.0
                ? "下一場 " + Mathf.CeilToInt((float)seconds) + " 秒後開始"
                : "";
        }

        public void Hide()
        {
            _confetti.Stop();
            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            for (int i = 0; i < _rowCount; i++)
            {
                float t = Mathf.Clamp01((now - _rowsShownAt - i * RowRevealDelay) / RowRevealSeconds);
                float eased = 1f - (1f - t) * (1f - t);
                _rows[i].Fade.alpha = eased;
                _rows[i].Root.anchoredPosition = new Vector2(Mathf.Lerp(60f, 0f, eased), RowY(i));
            }

            for (int i = 0; i < _cardCount; i++)
            {
                float t = Mathf.Clamp01((now - _cardsShownAt - CardRevealStart - i * CardRevealDelay) / CardRevealSeconds);
                _cards[i].Fade.alpha = t;
                _cards[i].Root.localScale = Vector3.one * EaseOutBack(t);
            }

            if (_settleMode)
            {
                float fade = Mathf.Clamp01((now - _cardsShownAt) / BackdropFadeSeconds);
                _backdrop.color = new Color(1f, 1f, 1f, fade);
                _backdropFallback.color = new Color(FallbackBackdrop.r, FallbackBackdrop.g, FallbackBackdrop.b,
                    FallbackBackdrop.a * fade);
            }
        }

        /// <summary>彈出時稍微超過再回到原尺寸，像卡片「啪」一聲蓋上來。</summary>
        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.7f;
            float shifted = t - 1f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted + overshoot * shifted * shifted;
        }

        // ---- 內容 ----

        private void FillRow(Row row, int position, int lane, RaceEngine race, HorseConfig[] lineup,
                             IReadOnlyList<ItemUsageLine> usage)
        {
            Sprite medal = ResultArt.Medal(position);
            row.Medal.sprite = medal;
            row.Medal.enabled = medal != null;
            row.Position.text = medal != null ? "" : (position + 1).ToString();

            bool known = lineup != null && lane < lineup.Length;
            row.Chip.color = known ? MaterialLibrary.ParseHex(lineup[lane].ColorHex, Color.gray) : Color.gray;
            row.Name.text = known ? lineup[lane].Name : "第 " + (lane + 1) + " 號";
            row.Name.color = position == 0 ? UiFactory.AccentColor : UiFactory.TextColor;
            row.Time.text = race != null ? race.Horses[lane].FinishTime.ToString("F2") + " 秒" : "";
            row.Usage.text = ItemStyle.FormatUsage(usage ?? new List<ItemUsageLine>(), lane);
        }

        private static void FillCard(Card card, Award award)
        {
            Sprite icon = ResultArt.AwardIcon(award.Kind);
            card.Icon.sprite = icon;
            card.Icon.enabled = icon != null;
            card.Title.text = AwardText.Title(award.Kind);
            card.Name.text = award.Nickname;
            card.Detail.text = AwardText.Detail(award);
        }

        private void LayoutCards()
        {
            float total = _cardCount * CardWidth + Mathf.Max(0, _cardCount - 1) * CardSpacing;
            float left = -total * 0.5f + CardWidth * 0.5f;
            for (int i = 0; i < _cardCount; i++)
            {
                _cards[i].Root.anchoredPosition = new Vector2(left + i * (CardWidth + CardSpacing), BottomPadding + CardHeight * 0.5f);
            }
        }

        /// <summary>衝線時沒有獎項區，面板縮短；結算時才加高放獎項卡片。</summary>
        private void ResizeBoard()
        {
            float height = HeaderHeight + _rowCount * RowHeight + BottomPadding
                           + (_settleMode && _cardCount > 0 ? AwardsAreaHeight : 0f);
            _board.sizeDelta = new Vector2(BoardWidth, height);
        }

        private static float RowY(int index)
        {
            return -(HeaderHeight + index * RowHeight);
        }

        private void SetBackdropVisible(bool visible)
        {
            bool hasArt = ResultArt.Background != null;
            _backdrop.gameObject.SetActive(visible && hasArt);
            _backdropFallback.gameObject.SetActive(visible && !hasArt);
        }

        // ---- 版面 ----

        private void BuildBackdrop()
        {
            RectTransform fallback = UiFactory.Panel(_root, "BackdropFallback", FallbackBackdrop);
            UiFactory.Stretch(fallback, 0f, 0f, 0f, 0f);
            _backdropFallback = fallback.GetComponent<Image>();
            fallback.gameObject.SetActive(false);

            RectTransform image = UiFactory.Node(_root, "Backdrop");
            UiFactory.Stretch(image, 0f, 0f, 0f, 0f);
            _backdrop = image.gameObject.AddComponent<RawImage>();
            _backdrop.texture = ResultArt.Background;
            _backdrop.raycastTarget = false;
            image.gameObject.SetActive(false);
        }

        private void BuildBoard(int laneCount)
        {
            _board = UiFactory.Node(_root, "Board");
            UiFactory.Place(_board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, BoardCenterY), new Vector2(BoardWidth, 600f));

            Image panel = _board.gameObject.AddComponent<Image>();
            panel.raycastTarget = false;
            if (ResultArt.Panel != null)
            {
                panel.sprite = ResultArt.Panel;
                panel.type = Image.Type.Sliced;
            }
            else
            {
                panel.color = UiFactory.PanelColorSolid;
            }

            BuildHeader();

            _rows = new Row[laneCount];
            for (int i = 0; i < laneCount; i++)
            {
                _rows[i] = BuildRow(i);
            }

            _cards = new Card[MaxAwardCards];
            for (int i = 0; i < MaxAwardCards; i++)
            {
                _cards[i] = BuildCard(i);
            }
        }

        private void BuildHeader()
        {
            Sprite ribbonSprite = ResultArt.Ribbon;
            RectTransform ribbon = UiFactory.Node(_board, "Ribbon");
            UiFactory.Place(ribbon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 20f), new Vector2(760f, 139f));
            if (ribbonSprite != null)
            {
                Image image = ribbon.gameObject.AddComponent<Image>();
                image.sprite = ribbonSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            _title = UiFactory.Label(ribbon, "Title", "名　次", 50, TextAnchor.MiddleCenter,
                ribbonSprite != null ? RibbonInk : UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Stretch((RectTransform)_title.transform, 0f, 10f, 0f, 24f);

            _subtitle = UiFactory.Label(_board, "Subtitle", "", 26, TextAnchor.MiddleCenter,
                UiFactory.MutedTextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_subtitle.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -142f), new Vector2(800f, 36f));
        }

        private Row BuildRow(int index)
        {
            Row row = new Row();
            row.Root = UiFactory.Node(_board, "Row_" + index);
            UiFactory.Place(row.Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, RowY(index)), new Vector2(BoardWidth - 160f, RowHeight - 6f));
            row.Fade = row.Root.gameObject.AddComponent<CanvasGroup>();

            RectTransform medal = UiFactory.Node(row.Root, "Medal");
            UiFactory.Place(medal, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(66f, 66f));
            row.Medal = medal.gameObject.AddComponent<Image>();
            row.Medal.preserveAspect = true;
            row.Medal.raycastTarget = false;

            row.Position = UiFactory.Label(row.Root, "Position", "", 40, TextAnchor.MiddleCenter,
                UiFactory.MutedTextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)row.Position.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(66f, 66f));

            RectTransform chip = UiFactory.Panel(row.Root, "Chip", Color.gray);
            UiFactory.Place(chip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(86f, 0f), new Vector2(12f, 52f));
            row.Chip = chip.GetComponent<Image>();

            row.Name = UiFactory.Label(row.Root, "Name", "", 40, TextAnchor.MiddleLeft, UiFactory.TextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)row.Name.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(116f, 14f), new Vector2(360f, 48f));

            row.Usage = UiFactory.Label(row.Root, "Usage", "", 22, TextAnchor.MiddleLeft, UiFactory.MutedTextColor);
            UiFactory.Place((RectTransform)row.Usage.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(116f, -22f), new Vector2(BoardWidth - 460f, 30f));
            UiFactory.ShrinkToFit(row.Usage, 14);

            row.Time = UiFactory.Label(row.Root, "Time", "", 36, TextAnchor.MiddleRight, UiFactory.TextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)row.Time.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(220f, 48f));

            return row;
        }

        private Card BuildCard(int index)
        {
            Card card = new Card();
            card.Root = UiFactory.Panel(_board, "Award_" + index, CardColor);
            UiFactory.Place(card.Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(CardWidth, CardHeight));
            card.Fade = card.Root.gameObject.AddComponent<CanvasGroup>();

            RectTransform icon = UiFactory.Node(card.Root, "Icon");
            UiFactory.Place(icon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(100f, 100f));
            card.Icon = icon.gameObject.AddComponent<Image>();
            card.Icon.preserveAspect = true;
            card.Icon.raycastTarget = false;

            card.Title = CardLabel(card.Root, "Title", 26, UiFactory.AccentColor, -110f);
            card.Name = CardLabel(card.Root, "Name", 32, UiFactory.TextColor, -146f);
            card.Detail = CardLabel(card.Root, "Detail", 22, UiFactory.MutedTextColor, -186f);

            card.Root.gameObject.SetActive(false);
            return card;
        }

        private static Text CardLabel(RectTransform parent, string name, int size, Color color, float y)
        {
            Text label = UiFactory.Label(parent, name, "", size, TextAnchor.MiddleCenter, color, FontStyle.Bold);
            UiFactory.Place((RectTransform)label.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(CardWidth - 16f, size + 12f));
            UiFactory.ShrinkToFit(label, 14);
            return label;
        }
    }
}
