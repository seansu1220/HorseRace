using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 用基本幾何體組出來的簡化馬（含騎師）。沒有指定 3D 模型、或模型載入失敗時使用。
    /// 所有部位掛在一個子節點下，上下起伏只移動這個子節點，不影響馬的位置與腳邊光環。
    /// </summary>
    public sealed class HorseBodyBlocky : IHorseBody
    {
        private const float BodyLength = 2.4f;
        private const float BodyHeight = 1.0f;
        private const float LegLength = 1.0f;
        private const float StrideAmplitude = 42f;
        private const float BaseStrideHz = 1.6f;
        private const float MaxStrideHz = 4.2f;

        private readonly Transform[] _legPivots = new Transform[4];
        private readonly Transform _root;
        private float _strideTime;

        public HorseBodyBlocky(Transform parent, Color coat)
        {
            GameObject root = new GameObject("BlockyBody");
            root.transform.SetParent(parent, false);
            _root = root.transform;
            Build(coat);
        }

        /// <summary>沿用原本的名牌高度（馬背上方約 2.6）。</summary>
        public float LabelHeight
        {
            get { return 2.6f; }
        }

        public float Length
        {
            get { return BodyLength; }
        }

        public void Animate(float speedRatio, float deltaTime)
        {
            float ratio = Mathf.Clamp01(speedRatio);
            float strideHz = Mathf.Lerp(BaseStrideHz, MaxStrideHz, ratio);
            _strideTime += deltaTime * strideHz;

            float bob = Mathf.Sin(_strideTime * Mathf.PI * 2f) * 0.06f * ratio;
            _root.localPosition = new Vector3(0f, bob, 0f);

            float amplitude = StrideAmplitude * Mathf.Max(0.15f, ratio);
            for (int i = 0; i < _legPivots.Length; i++)
            {
                // 對角腿同相位，做出接近奔馳的步態
                float phase = (i == 0 || i == 3) ? 0f : Mathf.PI;
                float angle = Mathf.Sin(_strideTime * Mathf.PI * 2f + phase) * amplitude;
                _legPivots[i].localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        public void ResetPose()
        {
            _strideTime = 0f;
            _root.localPosition = Vector3.zero;
        }

        public void Dispose()
        {
        }

        private void Build(Color coat)
        {
            Color mane = MaterialLibrary.Darken(coat, 0.45f);

            // 軀幹：把膠囊放倒沿著 X 軸，就是最省事又有辨識度的馬身
            Piece(PrimitiveType.Capsule, "Body", coat, new Vector3(0f, LegLength + BodyHeight * 0.5f, 0f),
                new Vector3(0f, 0f, 90f), new Vector3(BodyHeight, BodyLength * 0.5f, BodyHeight));

            // 脖子：從前胸往前上方斜出去
            Piece(PrimitiveType.Capsule, "Neck", coat, new Vector3(0.95f, LegLength + BodyHeight * 0.95f, 0f),
                new Vector3(0f, 0f, -35f), new Vector3(0.42f, 0.55f, 0.42f));

            // 頭：一個小方塊就夠，遠看很清楚
            Piece(PrimitiveType.Cube, "Head", coat, new Vector3(1.55f, LegLength + BodyHeight * 1.45f, 0f),
                new Vector3(0f, 0f, -18f), new Vector3(0.62f, 0.34f, 0.32f));

            Piece(PrimitiveType.Cube, "Mane", mane, new Vector3(0.85f, LegLength + BodyHeight * 1.35f, 0f),
                new Vector3(0f, 0f, -35f), new Vector3(0.7f, 0.18f, 0.36f));

            Piece(PrimitiveType.Cube, "Tail", mane, new Vector3(-1.2f, LegLength + BodyHeight * 0.85f, 0f),
                new Vector3(0f, 0f, 32f), new Vector3(0.62f, 0.16f, 0.24f));

            // 騎師：騎師服用馬匹代表色的亮版，讓大螢幕上更容易分辨
            Piece(PrimitiveType.Cube, "Jockey", MaterialLibrary.Lighten(coat, 0.35f),
                new Vector3(-0.1f, LegLength + BodyHeight * 1.35f, 0f), Vector3.zero, new Vector3(0.42f, 0.55f, 0.42f));

            Piece(PrimitiveType.Sphere, "Helmet", Color.white,
                new Vector3(-0.1f, LegLength + BodyHeight * 1.78f, 0f), Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f));

            BuildLegs(new Color(0.16f, 0.14f, 0.13f));
        }

        private void BuildLegs(Color hoofColor)
        {
            float[] legX = { 0.72f, 0.72f, -0.72f, -0.72f };
            float[] legZ = { 0.3f, -0.3f, 0.3f, -0.3f };

            for (int i = 0; i < 4; i++)
            {
                // 樞紐放在髖部，旋轉樞紐就能讓整條腿前後擺動
                GameObject pivot = new GameObject("LegPivot_" + i);
                pivot.transform.SetParent(_root, false);
                pivot.transform.localPosition = new Vector3(legX[i], LegLength, legZ[i]);
                _legPivots[i] = pivot.transform;

                GameObject leg = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Leg", pivot.transform, hoofColor, true);
                leg.transform.localPosition = new Vector3(0f, -LegLength * 0.5f, 0f);
                leg.transform.localScale = new Vector3(0.2f, LegLength, 0.2f);
            }
        }

        private void Piece(PrimitiveType type, string name, Color color,
                           Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            GameObject piece = TrackBuilder.CreatePrimitive(type, name, _root, color, true);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = Quaternion.Euler(localEuler);
            piece.transform.localScale = localScale;
        }
    }
}
