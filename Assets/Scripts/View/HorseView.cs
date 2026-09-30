using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 一匹馬在場上的呈現：位置（對模擬結果做平滑）、腳邊的道具光環、名牌掛點。
    /// 長相與跑步動作交給 <see cref="IHorseBody"/>：有設定 3D 模型就用 <see cref="HorseBodyModel"/>，
    /// 沒有或載入失敗就用 <see cref="HorseBodyBlocky"/>。換模型只動設定檔，Core 與 Net 完全不受影響。
    /// </summary>
    public sealed class HorseView : MonoBehaviour
    {
        /// <summary>畫面位置追上模擬位置的速率。模擬是 50 Hz，畫面用這個把階梯狀的位移抹平。</summary>
        private const float PositionFollowRate = 22f;

        /// <summary>光環直徑相對身長的比例，下限是原本方塊馬用的 2.6。</summary>
        private const float AuraLengthRatio = 1.1f;
        private const float MinAuraDiameter = 2.6f;

        private IHorseBody _body;
        private Transform _aura;
        private Renderer _auraRenderer;
        private float _currentX;
        private float _laneZ;

        public int Lane { get; private set; }

        /// <summary>名牌要掛的世界座標（馬背上方）。</summary>
        public Vector3 LabelAnchor
        {
            get { return transform.position + Vector3.up * _body.LabelHeight; }
        }

        public static HorseView Create(Transform parent, int lane, int laneCount, HorseConfig config,
                                       HorseModelConfig modelConfig)
        {
            GameObject root = new GameObject("Horse_" + lane + "_" + config.Name);
            root.transform.SetParent(parent, false);

            HorseView view = root.AddComponent<HorseView>();
            view.Lane = lane;
            view._laneZ = TrackLayout.LaneZ(lane, laneCount);
            view.BuildBody(config, modelConfig);
            view.BuildAura();
            view.ResetToGate();
            return view;
        }

        /// <summary>把馬放回起跑閘，並清掉動畫狀態。換場時呼叫。</summary>
        public void ResetToGate()
        {
            _currentX = TrackLayout.StartX;
            _body.ResetPose();
            transform.position = new Vector3(_currentX, TrackLayout.GroundY, _laneZ);
            SetAura(false, Color.white);
        }

        /// <summary>依模擬結果更新位置與動作。</summary>
        /// <param name="progress01">賽程完成度 0~1。</param>
        /// <param name="speedRatio">目前速度相對基礎速度的比值，用來決定步頻或動畫速度。</param>
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

            transform.position = new Vector3(_currentX, TrackLayout.GroundY, _laneZ);
            _body.Animate(speedRatio, deltaTime);
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

        private void OnDestroy()
        {
            if (_body != null)
            {
                _body.Dispose();
            }
        }

        private void BuildBody(HorseConfig config, HorseModelConfig modelConfig)
        {
            Color coat = MaterialLibrary.ParseHex(config.ColorHex, Color.gray);
            string modelPath = modelConfig != null ? modelConfig.ResolveModelPath(config) : "";

            if (!string.IsNullOrEmpty(modelPath))
            {
                _body = HorseBodyModel.TryCreate(transform, modelPath, modelConfig, coat);
            }

            if (_body == null)
            {
                _body = new HorseBodyBlocky(transform, coat);
            }
        }

        private void BuildAura()
        {
            // 道具生效時在腳邊亮一圈。用圓柱壓扁而不是罩住整匹馬，才不會把馬本體遮掉
            float diameter = Mathf.Max(MinAuraDiameter, _body.Length * AuraLengthRatio);
            GameObject aura = TrackBuilder.CreatePrimitive(PrimitiveType.Cylinder, "Aura", transform, Color.white);
            aura.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            aura.transform.localScale = new Vector3(diameter, 0.04f, diameter);

            _aura = aura.transform;
            _auraRenderer = aura.GetComponent<Renderer>();
            aura.SetActive(false);
        }
    }
}
