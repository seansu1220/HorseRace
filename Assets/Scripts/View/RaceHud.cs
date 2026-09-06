using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 大螢幕的介面。整份在執行期以程式建立，場景中不需要放任何 Canvas。
    /// 這一層只負責顯示，不做任何判斷邏輯——所有數值都由 RaceDirector 餵進來。
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {
        private const float RowHeight = 78f;
        private const float BarWidth = 250f;
        private const float PanelWidth = 560f;

        /// <summary>名單面板一列的所有元件。</summary>
        private sealed class HorseRow
        {
            public RectTransform Root;
            public Image Chip;
            public Text Position;
            public Text Name;
            public Text Value;
            public RectTransform BarFill;
        }

        private Canvas _canvas;
        private RectTransform _canvasRect;

        private Text _raceLabel;
        private Text _phaseLabel;
        private Text _countdownLabel;

        private Text _listTitle;
        private HorseRow[] _rows;

        private RectTransform _resultPanel;
        private Text[] _resultRows;
        private Text _resultTitle;

        private RectTransform _nameTagLayer;
        private Text[] _nameTags;

        private HorseConfig[] _lineup;

        public void Build(int laneCount)
        {
            _canvas = UiFactory.CreateCanvas("RaceHud", 0);
            _canvas.transform.SetParent(transform, false);
            _canvasRect = (RectTransform)_canvas.transform;

            // 建立順序就是繪製順序：uGUI 中後建立的兄弟節點蓋在先建立的上面。
            // 名牌是貼在 3D 馬匹頭上的世界座標標籤，必須是最底層，
            // 否則會蓋住名次揭曉面板與左側名單，把字擋掉。
            BuildNameTags(laneCount);
            BuildTopBar();
            BuildHorseList(laneCount);
            BuildJoinPlaceholder();
            BuildHint();
            BuildResultPanel(laneCount);
        }

        /// <summary>換場時更新名單。顏色與名字只有這時候會變。</summary>
        public void SetLineup(HorseConfig[] lineup)
        {
            _lineup = lineup;

            for (int lane = 0; lane < _rows.Length; lane++)
            {
                bool active = lane < lineup.Length;
                _rows[lane].Root.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                Color coat = MaterialLibrary.ParseHex(lineup[lane].ColorHex, Color.gray);
                _rows[lane].Chip.color = coat;
                _rows[lane].Name.text = lineup[lane].Name;
                _rows[lane].Position.text = (lane + 1).ToString();
                _rows[lane].Value.text = "—";
                UiFactory.SetProgress(_rows[lane].BarFill, 0f, BarWidth);

                _nameTags[lane].text = lineup[lane].Name;
                _nameTags[lane].color = coat;
            }
        }

        public void ShowPhase(RacePhase phase, double remainingSeconds, int raceNumber)
        {
            _raceLabel.text = "第 " + raceNumber + " 場";
            _phaseLabel.text = PhaseTitle(phase);
            _phaseLabel.color = PhaseColor(phase);

            if (phase == RacePhase.Racing)
            {
                _countdownLabel.text = "";
            }
            else
            {
                _countdownLabel.text = Mathf.CeilToInt((float)remainingSeconds).ToString();
            }

            _listTitle.text = phase == RacePhase.Racing ? "即時名次" : "出賽名單 ／ 賠率";
        }

        /// <summary>下注階段顯示賠率。傳入 null 代表還在計算中。</summary>
        public void ShowOdds(double[] odds)
        {
            for (int lane = 0; lane < _rows.Length; lane++)
            {
                if (!_rows[lane].Root.gameObject.activeSelf)
                {
                    continue;
                }

                _rows[lane].Position.text = (lane + 1).ToString();
                _rows[lane].Position.color = UiFactory.MutedTextColor;
                _rows[lane].Value.text = odds == null || lane >= odds.Length
                    ? "計算中"
                    : odds[lane].ToString("F2") + " 倍";
                _rows[lane].Value.color = odds == null ? UiFactory.MutedTextColor : UiFactory.AccentColor;
                UiFactory.SetProgress(_rows[lane].BarFill, 0f, BarWidth);
            }
        }

        /// <summary>賽中顯示名次與完成度。</summary>
        public void ShowLiveRanks(RaceEngine race, int[] ranksByLane)
        {
            for (int lane = 0; lane < _rows.Length; lane++)
            {
                if (!_rows[lane].Root.gameObject.activeSelf || lane >= race.HorseCount)
                {
                    continue;
                }

                HorseState horse = race.Horses[lane];
                int rank = ranksByLane[lane];

                _rows[lane].Position.text = rank.ToString();
                _rows[lane].Position.color = rank == 1 ? UiFactory.AccentColor : UiFactory.TextColor;

                // 完賽後顯示成績；還在跑的顯示完成度。全部顯示 100% 沒有任何資訊量
                _rows[lane].Value.text = horse.Finished
                    ? horse.FinishTime.ToString("F2") + " 秒"
                    : Mathf.RoundToInt((float)horse.Progress01 * 100f) + "%";
                _rows[lane].Value.color = UiFactory.TextColor;
                UiFactory.SetProgress(_rows[lane].BarFill, (float)horse.Progress01, BarWidth);
            }
        }

        /// <summary>揭曉名次。</summary>
        public void ShowResult(RaceEngine race, int[] finishOrder)
        {
            _resultPanel.gameObject.SetActive(true);
            _resultTitle.text = "名 次";

            for (int position = 0; position < _resultRows.Length; position++)
            {
                bool active = position < finishOrder.Length;
                _resultRows[position].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                int lane = finishOrder[position];
                HorseState horse = race.Horses[lane];
                string horseName = _lineup != null && lane < _lineup.Length
                    ? _lineup[lane].Name
                    : "第 " + (lane + 1) + " 號";

                _resultRows[position].text = string.Format(
                    "{0}.   {1,-6}   {2:F2} 秒", position + 1, horseName, horse.FinishTime);
                _resultRows[position].color = position == 0
                    ? UiFactory.AccentColor
                    : UiFactory.TextColor;
            }
        }

        public void HideResult()
        {
            _resultPanel.gameObject.SetActive(false);
        }

        /// <summary>把名牌貼到每匹馬的頭上。用螢幕座標而非世界空間 Canvas，省下每匹馬一個 Canvas。</summary>
        public void UpdateNameTags(HorseView[] horses, Camera camera, bool visible)
        {
            for (int lane = 0; lane < _nameTags.Length; lane++)
            {
                bool show = visible && lane < horses.Length && horses[lane] != null;
                if (!show)
                {
                    _nameTags[lane].gameObject.SetActive(false);
                    continue;
                }

                Vector3 screenPoint = camera.WorldToScreenPoint(horses[lane].LabelAnchor);
                if (screenPoint.z <= 0f)
                {
                    _nameTags[lane].gameObject.SetActive(false);
                    continue;
                }

                Vector2 localPoint;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvasRect, screenPoint, null, out localPoint))
                {
                    _nameTags[lane].gameObject.SetActive(false);
                    continue;
                }

                _nameTags[lane].gameObject.SetActive(true);
                ((RectTransform)_nameTags[lane].transform).anchoredPosition = localPoint;
            }
        }

        // ---- 建構 ----

        private void BuildTopBar()
        {
            RectTransform bar = UiFactory.Panel(_canvas.transform, "TopBar", UiFactory.PanelColor);
            UiFactory.Place(bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(560f, 168f));

            _raceLabel = UiFactory.Label(bar, "RaceLabel", "第 1 場", 30,
                TextAnchor.UpperCenter, UiFactory.MutedTextColor);
            UiFactory.Place((RectTransform)_raceLabel.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(520f, 40f));

            _phaseLabel = UiFactory.Label(bar, "PhaseLabel", "準備中", 44,
                TextAnchor.UpperCenter, UiFactory.TextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_phaseLabel.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(520f, 56f));

            _countdownLabel = UiFactory.Label(bar, "Countdown", "", 62,
                TextAnchor.UpperCenter, UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_countdownLabel.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(520f, 68f));
        }

        private void BuildHorseList(int laneCount)
        {
            float panelHeight = 96f + laneCount * RowHeight;

            RectTransform panel = UiFactory.Panel(_canvas.transform, "HorseList", UiFactory.PanelColor);
            UiFactory.Place(panel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(36f, -36f), new Vector2(PanelWidth, panelHeight));

            _listTitle = UiFactory.Label(panel, "Title", "出賽名單 ／ 賠率", 30,
                TextAnchor.UpperLeft, UiFactory.MutedTextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_listTitle.transform, new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(400f, 40f));

            _rows = new HorseRow[laneCount];
            for (int lane = 0; lane < laneCount; lane++)
            {
                _rows[lane] = BuildHorseRow(panel, lane);
            }
        }

        private HorseRow BuildHorseRow(Transform parent, int lane)
        {
            HorseRow row = new HorseRow();

            row.Root = UiFactory.Node(parent, "Row_" + lane);
            UiFactory.Place(row.Root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -(80f + lane * RowHeight)), new Vector2(PanelWidth, RowHeight));

            row.Position = UiFactory.Label(row.Root, "Position", "1", 40,
                TextAnchor.MiddleCenter, UiFactory.TextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)row.Position.transform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(24f, 12f), new Vector2(48f, 48f));

            RectTransform chip = UiFactory.Panel(row.Root, "Chip", Color.gray);
            UiFactory.Place(chip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(80f, 12f), new Vector2(30f, 30f));
            row.Chip = chip.GetComponent<Image>();

            row.Name = UiFactory.Label(row.Root, "Name", "—", 32,
                TextAnchor.MiddleLeft, UiFactory.TextColor);
            UiFactory.Place((RectTransform)row.Name.transform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(124f, 12f), new Vector2(180f, 40f));

            row.Value = UiFactory.Label(row.Root, "Value", "—", 30,
                TextAnchor.MiddleRight, UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)row.Value.transform, new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(PanelWidth - 28f, 12f), new Vector2(180f, 40f));

            row.BarFill = UiFactory.ProgressBar(row.Root, "Bar",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(124f, -18f), new Vector2(BarWidth, 8f),
                UiFactory.AccentColor);

            return row;
        }

        private void BuildResultPanel(int laneCount)
        {
            float height = 130f + laneCount * 62f;

            _resultPanel = UiFactory.Panel(_canvas.transform, "ResultPanel", UiFactory.PanelColorSolid);
            UiFactory.Place(_resultPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -30f), new Vector2(700f, height));

            _resultTitle = UiFactory.Label(_resultPanel, "Title", "名 次", 40,
                TextAnchor.UpperCenter, UiFactory.AccentColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_resultTitle.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(640f, 52f));

            _resultRows = new Text[laneCount];
            for (int position = 0; position < laneCount; position++)
            {
                _resultRows[position] = UiFactory.Label(_resultPanel, "Result_" + position, "", 34,
                    TextAnchor.MiddleLeft, UiFactory.TextColor);
                UiFactory.Place((RectTransform)_resultRows[position].transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(72f, -(100f + position * 62f)), new Vector2(560f, 52f));
            }

            _resultPanel.gameObject.SetActive(false);
        }

        private void BuildJoinPlaceholder()
        {
            // M3 會把這塊換成真正的房號與 QRCode
            RectTransform panel = UiFactory.Panel(_canvas.transform, "JoinPanel", UiFactory.PanelColor);
            UiFactory.Place(panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-36f, -36f), new Vector2(360f, 400f));

            Text title = UiFactory.Label(panel, "Title", "手機下注", 32,
                TextAnchor.UpperCenter, UiFactory.MutedTextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)title.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(320f, 44f));

            RectTransform placeholder = UiFactory.Panel(
                panel, "QrPlaceholder", new Color(1f, 1f, 1f, 0.08f));
            UiFactory.Place(placeholder, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(240f, 240f));

            Text hint = UiFactory.Label(placeholder, "Hint", "QRCode\n（M3 實作）", 26,
                TextAnchor.MiddleCenter, UiFactory.MutedTextColor);
            UiFactory.Stretch((RectTransform)hint.transform, 8f, 8f, 8f, 8f);

            Text note = UiFactory.Label(panel, "Note", "掃碼即可加入下注", 24,
                TextAnchor.UpperCenter, UiFactory.MutedTextColor);
            UiFactory.Place((RectTransform)note.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -334f), new Vector2(320f, 40f));
        }

        private void BuildHint()
        {
            Text hint = UiFactory.Label(_canvas.transform, "DebugHint",
                "[空白鍵] 跳過階段    [R] 重新開始    [1] 對隨機一匹加速    [2] 對隨機一匹減速    [Esc] 離開",
                22, TextAnchor.LowerLeft, UiFactory.MutedTextColor);
            UiFactory.Place((RectTransform)hint.transform, new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(36f, 28f), new Vector2(1200f, 34f));
        }

        private void BuildNameTags(int laneCount)
        {
            _nameTagLayer = UiFactory.Node(_canvas.transform, "NameTags");
            UiFactory.Stretch(_nameTagLayer, 0f, 0f, 0f, 0f);

            _nameTags = new Text[laneCount];
            for (int lane = 0; lane < laneCount; lane++)
            {
                _nameTags[lane] = UiFactory.Label(_nameTagLayer, "Tag_" + lane, "", 28,
                    TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);

                RectTransform rect = (RectTransform)_nameTags[lane].transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(220f, 36f);

                Outline outline = _nameTags[lane].gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
            }
        }

        private static string PhaseTitle(RacePhase phase)
        {
            switch (phase)
            {
                case RacePhase.Idle:
                    return "準備下一場";
                case RacePhase.Betting:
                    return "下注中";
                case RacePhase.Racing:
                    return "比賽進行中";
                case RacePhase.Photo:
                    return "衝線";
                case RacePhase.Settle:
                    return "結算";
                default:
                    return "";
            }
        }

        private static Color PhaseColor(RacePhase phase)
        {
            switch (phase)
            {
                case RacePhase.Betting:
                    return new Color(0.42f, 0.85f, 0.52f);
                case RacePhase.Racing:
                    return new Color(0.98f, 0.55f, 0.36f);
                default:
                    return UiFactory.TextColor;
            }
        }
    }
}
