using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 一匹馬的視覺呈現。用基本幾何體組出來的簡化馬型，
    /// 之後換成正式模型時只要改這個檔案，Core 與 Net 完全不受影響。
    /// </summary>
    public sealed class HorseView : MonoBehaviour
    {
        private const float BodyLength = 2.4f;
        private const float BodyHeight = 1.0f;
        private const float LegLength = 1.0f;
        private const float StrideAmplitude = 42f;
        private const float BaseStrideHz = 1.6f;
        private const float MaxStrideHz = 4.2f;

        /// <summary>畫面位置追上模擬位置的速率。模擬是 50 Hz，畫面用這個把階梯狀的位移抹平。</summary>
        private const float PositionFollowRate = 22f;

        private readonly Transform[] _legPivots = new Transform[4];

        private Transform _body;
        private Transform _aura;
        private Renderer _auraRenderer;
        private float _strideTime;
        private float _currentX;
        private float _laneZ;

        public int Lane { get; private set; }

        /// <summary>名牌要掛的世界座標（馬背上方）。</summary>
        public Vector3 LabelAnchor
        {
            get { return transform.position + Vector3.up * 2.6f; }
        }

        public static HorseView Create(Transform parent, int lane, int laneCount, HorseConfig config)
        {
            GameObject root = new GameObject("Horse_" + lane + "_" + config.Name);
            root.transform.SetParent(parent, false);

            HorseView view = root.AddComponent<HorseView>();
            view.Lane = lane;
            view._laneZ = TrackLayout.LaneZ(lane, laneCount);
            view.BuildVisual(config);
            view.ResetToGate();
            return view;
        }

        /// <summary>把馬放回起跑閘，並清掉動畫狀態。換場時呼叫。</summary>
        public void ResetToGate()
        {
            _currentX = TrackLayout.StartX;
            _strideTime = 0f;
            transform.position = new Vector3(_currentX, TrackLayout.GroundY, _laneZ);
            SetAura(false, Color.white);
        }

        /// <summary>
        /// 依模擬結果更新位置與動作。
        /// </summary>
        /// <param name="progress01">賽程完成度 0~1。</param>
        /// <param name="speedRatio">目前速度相對基礎速度的比值，用來決定步頻。</param>
        /// <param name="deltaTime">這一幀經過的秒數。</param>
        public void UpdateVisual(float progress01, float speedRatio, float deltaTime)
        {
            float targetX = TrackLayout.ProgressToX(progress01);

            // 差距過大代表換場或跳關，直接瞬移，不要讓馬用滑的橫越整條賽道
            if (Mathf.Abs(targetX - _currentX) > TrackLayout.VisualLength * 0.25f)
            {
                _currentX = targetX;
            }
            else
            {
                float follow = 1f - Mathf.Exp(-PositionFollowRate * deltaTime);
                _currentX = Mathf.Lerp(_currentX, targetX, follow);
            }

            float strideHz = Mathf.Lerp(BaseStrideHz, MaxStrideHz, Mathf.Clamp01(speedRatio));
            _strideTime += deltaTime * strideHz;

            float bob = Mathf.Sin(_strideTime * Mathf.PI * 2f) * 0.06f * Mathf.Clamp01(speedRatio);
            transform.position = new Vector3(_currentX, TrackLayout.GroundY + bob, _laneZ);

            AnimateLegs(Mathf.Clamp01(speedRatio));
        }

        /// <summary>顯示／隱藏道具生效的光環。</summary>
        public void SetAura(bool active, Color color)
        {
            if (_aura == null)
            {
                return;
            }

            _aura.gameObject.SetActive(active);
            if (active && _auraRenderer != null)
            {
                _auraRenderer.sharedMaterial = MaterialLibrary.Opaque(color, 0.6f);
            }
        }

        private void AnimateLegs(float speedRatio)
        {
            float amplitude = StrideAmplitude * Mathf.Max(0.15f, speedRatio);
            for (int i = 0; i < _legPivots.Length; i++)
            {
                if (_legPivots[i] == null)
                {
                    continue;
                }

                // 對角腿同相位，做出接近奔馳的步態
                float phase = (i == 0 || i == 3) ? 0f : Mathf.PI;
                float angle = Mathf.Sin(_strideTime * Mathf.PI * 2f + phase) * amplitude;
                _legPivots[i].localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void BuildVisual(HorseConfig config)
        {
            Color coat = MaterialLibrary.ParseHex(config.ColorHex, Color.gray);
            Color mane = MaterialLibrary.Darken(coat, 0.45f);
            Color hoof = new Color(0.16f, 0.14f, 0.13f);

            // 軀幹：把膠囊放倒沿著 X 軸，就是最省事又有辨識度的馬身
            _body = Piece(PrimitiveType.Capsule, "Body", coat,
                new Vector3(0f, LegLength + BodyHeight * 0.5f, 0f),
                new Vector3(0f, 0f, 90f),
                new Vector3(BodyHeight, BodyLength * 0.5f, BodyHeight));

            // 脖子：從前胸往前上方斜出去
            Piece(PrimitiveType.Capsule, "Neck", coat,
                new Vector3(0.95f, LegLength + BodyHeight * 0.95f, 0f),
                new Vector3(0f, 0f, -35f),
                new Vector3(0.42f, 0.55f, 0.42f));

            // 頭：一個小方塊就夠，遠看很清楚
            Piece(PrimitiveType.Cube, "Head", coat,
                new Vector3(1.55f, LegLength + BodyHeight * 1.45f, 0f),
                new Vector3(0f, 0f, -18f),
                new Vector3(0.62f, 0.34f, 0.32f));

            Piece(PrimitiveType.Cube, "Mane", mane,
                new Vector3(0.85f, LegLength + BodyHeight * 1.35f, 0f),
                new Vector3(0f, 0f, -35f),
                new Vector3(0.7f, 0.18f, 0.36f));

            Piece(PrimitiveType.Cube, "Tail", mane,
                new Vector3(-1.2f, LegLength + BodyHeight * 0.85f, 0f),
                new Vector3(0f, 0f, 32f),
                new Vector3(0.62f, 0.16f, 0.24f));

            // 騎師：騎師服用馬匹代表色的亮版，讓大螢幕上更容易分辨
            Piece(PrimitiveType.Cube, "Jockey", MaterialLibrary.Lighten(coat, 0.35f),
                new Vector3(-0.1f, LegLength + BodyHeight * 1.35f, 0f),
                Vector3.zero,
                new Vector3(0.42f, 0.55f, 0.42f));

            Piece(PrimitiveType.Sphere, "Helmet", Color.white,
                new Vector3(-0.1f, LegLength + BodyHeight * 1.78f, 0f),
                Vector3.zero,
                new Vector3(0.34f, 0.34f, 0.34f));

            BuildLegs(hoof);
            BuildAura();
        }

        private void BuildLegs(Color hoofColor)
        {
            float[] legX = { 0.72f, 0.72f, -0.72f, -0.72f };
            float[] legZ = { 0.3f, -0.3f, 0.3f, -0.3f };

            for (int i = 0; i < 4; i++)
            {
                // 樞紐放在髖部，旋轉樞紐就能讓整條腿前後擺動
                GameObject pivot = new GameObject("LegPivot_" + i);
                pivot.transform.SetParent(transform, false);
                pivot.transform.localPosition = new Vector3(legX[i], LegLength, legZ[i]);
                _legPivots[i] = pivot.transform;

                GameObject leg = TrackBuilder.CreatePrimitive(
                    PrimitiveType.Cube, "Leg", pivot.transform, hoofColor, true);
                leg.transform.localPosition = new Vector3(0f, -LegLength * 0.5f, 0f);
                leg.transform.localScale = new Vector3(0.2f, LegLength, 0.2f);
            }
        }

        private void BuildAura()
        {
            // 道具生效時在腳邊亮一圈。用圓柱壓扁而不是罩住整匹馬，才不會把馬本體遮掉
            GameObject aura = TrackBuilder.CreatePrimitive(
                PrimitiveType.Cylinder, "Aura", transform, Color.white);
            aura.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            aura.transform.localScale = new Vector3(2.6f, 0.04f, 2.6f);

            _aura = aura.transform;
            _auraRenderer = aura.GetComponent<Renderer>();
            aura.SetActive(false);
        }

        private Transform Piece(
            PrimitiveType type, string name, Color color,
            Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            GameObject piece = TrackBuilder.CreatePrimitive(type, name, transform, color, true);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = Quaternion.Euler(localEuler);
            piece.transform.localScale = localScale;
            return piece.transform;
        }
    }
}
