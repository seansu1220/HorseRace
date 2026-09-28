using UnityEngine;
using UnityEngine.UI;

namespace HorseRace.View
{
    /// <summary>
    /// 結算時從畫面上方飄落的彩帶。固定數量的碎片循環使用：落到底下就從上面重新出現，
    /// 停止時已在畫面上的會落完才消失，不會一瞬間全部不見。
    /// 碎片圖案取自 <see cref="ResultArt.Confetti"/>，沒有素材時改用純色小方塊。
    /// </summary>
    public sealed class ConfettiRain : MonoBehaviour
    {
        private static readonly Color[] FallbackColors =
        {
            new Color(0.89f, 0.71f, 0.29f), new Color(0.82f, 0.23f, 0.23f),
            new Color(0.24f, 0.50f, 0.82f), new Color(0.27f, 0.69f, 0.42f)
        };

        private sealed class Piece
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Spin;
            public float SwayPhase;
            public bool Falling;
        }

        private RectTransform _area;
        private Piece[] _pieces;
        private bool _playing;
        private System.Random _random;

        public static ConfettiRain Build(RectTransform parent, int count)
        {
            RectTransform area = UiFactory.Node(parent, "Confetti");
            UiFactory.Stretch(area, 0f, 0f, 0f, 0f);

            ConfettiRain rain = area.gameObject.AddComponent<ConfettiRain>();
            rain._area = area;
            rain._random = new System.Random(20260928);
            rain._pieces = new Piece[count];

            Sprite[] sprites = ResultArt.Confetti;
            for (int i = 0; i < count; i++)
            {
                RectTransform rect = UiFactory.Node(area, "Piece_" + i);
                Image image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                if (sprites.Length > 0)
                {
                    image.sprite = sprites[i % sprites.Length];
                    image.preserveAspect = true;
                }
                else
                {
                    image.color = FallbackColors[i % FallbackColors.Length];
                }

                rect.gameObject.SetActive(false);
                rain._pieces[i] = new Piece { Rect = rect, Image = image };
            }

            return rain;
        }

        /// <summary>開始飄落：碎片從畫面上方不同高度出發，不會整排一起掉下來。</summary>
        public void Play()
        {
            _playing = true;
            foreach (Piece piece in _pieces)
            {
                Respawn(piece, (float)_random.NextDouble());
            }
        }

        /// <summary>停止產生新的碎片，並立即隱藏（換場時不需要殘留）。</summary>
        public void Stop()
        {
            _playing = false;
            foreach (Piece piece in _pieces)
            {
                piece.Falling = false;
                piece.Rect.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float bottom = -_area.rect.height * 0.5f - 60f;

            foreach (Piece piece in _pieces)
            {
                if (!piece.Falling)
                {
                    continue;
                }

                piece.SwayPhase += dt * 2.2f;
                Vector2 position = piece.Rect.anchoredPosition;
                position += new Vector2(piece.Velocity.x + Mathf.Sin(piece.SwayPhase) * 40f, piece.Velocity.y) * dt;
                piece.Rect.anchoredPosition = position;
                piece.Rect.localRotation = Quaternion.Euler(0f, 0f, piece.Rect.localEulerAngles.z + piece.Spin * dt);

                if (position.y < bottom)
                {
                    Respawn(piece, 0f);
                }
            }
        }

        /// <param name="extraHeight">0～1：往畫面上方多推多遠，讓第一波碎片錯開出現。</param>
        private void Respawn(Piece piece, float extraHeight)
        {
            Rect area = _area.rect;
            float size = 26f + (float)_random.NextDouble() * 22f;
            float x = ((float)_random.NextDouble() - 0.5f) * area.width;
            float y = area.height * 0.5f + 40f + extraHeight * area.height;

            piece.Rect.sizeDelta = new Vector2(size, size);
            piece.Rect.anchoredPosition = new Vector2(x, y);
            piece.Velocity = new Vector2(((float)_random.NextDouble() - 0.5f) * 60f,
                -(160f + (float)_random.NextDouble() * 200f));
            piece.Spin = ((float)_random.NextDouble() - 0.5f) * 360f;
            piece.SwayPhase = (float)_random.NextDouble() * 6.28f;
            piece.Falling = true;
            piece.Rect.gameObject.SetActive(true);
        }
    }
}
