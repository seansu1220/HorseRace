using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 用程式建立 uGUI 元件的共用工具。整套介面都在執行期生成，
    /// 場景裡不需要放任何東西，也不需要在 Inspector 拉引用。
    /// </summary>
    public static class UiFactory
    {
        /// <summary>介面的設計解析度。所有座標數值都以這個尺寸為基準。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.09f, 0.74f);
        public static readonly Color PanelColorSolid = new Color(0.05f, 0.06f, 0.09f, 0.93f);
        public static readonly Color TextColor = new Color(0.94f, 0.95f, 0.97f);
        public static readonly Color MutedTextColor = new Color(0.62f, 0.66f, 0.74f);
        public static readonly Color AccentColor = new Color(0.96f, 0.77f, 0.26f);

        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            GameObject root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // 大螢幕的長寬比不一定是 16:9，取寬高的折衷值才不會有一邊被裁掉
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>建立一個空的 UI 節點。</summary>
        public static RectTransform Node(Transform parent, string name)
        {
            GameObject node = new GameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent, false);
            return (RectTransform)node.transform;
        }

        /// <summary>建立一塊底板。</summary>
        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            RectTransform rect = Node(parent, name);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>建立一段文字。</summary>
        public static Text Label(
            Transform parent, string name, string content, int fontSize,
            TextAnchor anchor, Color color, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Node(parent, name);

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = FontProvider.Get();
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>以固定尺寸擺放：指定錨點、樞紐、相對位置與大小。</summary>
        public static void Place(
            RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>撐滿父容器並留出四邊邊界。</summary>
        public static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>建立一條可填充的進度條，回傳負責填充的 RectTransform。</summary>
        public static RectTransform ProgressBar(
            Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color fillColor)
        {
            RectTransform background = Panel(parent, name, new Color(1f, 1f, 1f, 0.10f));
            Place(background, anchor, pivot, position, size);

            RectTransform fill = Panel(background, "Fill", fillColor);
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fill.sizeDelta = new Vector2(0f, 0f);

            return fill;
        }

        /// <summary>設定進度條的填充比例 0~1。</summary>
        public static void SetProgress(RectTransform fill, float ratio, float fullWidth)
        {
            Vector2 size = fill.sizeDelta;
            size.x = Mathf.Clamp01(ratio) * fullWidth;
            fill.sizeDelta = size;
        }
    }
}
