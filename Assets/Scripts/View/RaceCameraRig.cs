using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 攝影機運鏡。三種鏡位：起跑閘（正面或側面）、賽中側面跟拍、衝線特寫。
    /// 全部以平滑逼近的方式移動，避免大螢幕上的畫面突兀跳動。
    ///
    /// 起跑閘用正面鏡位時，開跑後先停在正面讓觀眾看到馬衝出閘門，再沿一道弧線緩入緩出地移到側面跟拍：
    /// 先往鏡頭側（−Z）拉開、再轉到側面，不會直線穿過迎面跑來的馬群。
    /// </summary>
    public sealed class RaceCameraRig : MonoBehaviour
    {
        private const float FollowRate = 3.2f;
        private const float SnapRate = 40f;

        private Camera _camera;
        private float _laneSpan;

        private Vector3 _targetPosition;
        private Vector3 _targetLookAt;

        // ---- 起跑正面鏡位與開跑轉場 ----
        private bool _startFromFront = true;
        private float _startHoldSeconds = 1f;
        private float _startBlendSeconds = 2.5f;
        private bool _transitioning;
        private float _transitionStartTime;

        /// <summary>正面鏡位離起跑線多遠、多高。夠遠才框得進整個閘門，也讓馬衝出後有幾秒才跑到鏡頭前。</summary>
        private const float FrontDistance = 26f;
        private const float FrontHeight = 3.6f;

        /// <summary>稍微偏向鏡頭側，閘門不會被拍成完全對稱的死板正面。</summary>
        private const float FrontSideOffset = 2f;

        /// <summary>轉場時側移（Z）比前後（X）快多少倍，決定弧線有多「先拉開」。</summary>
        private const float SideLeadFactor = 1.6f;

        public void Configure(Camera camera, int laneCount)
        {
            _camera = camera;
            _laneSpan = TrackLayout.LaneSpan(laneCount);
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 600f;

            FrameGate();
            SnapToTarget();
        }

        /// <summary>
        /// 設定起跑鏡位：<paramref name="fromFront"/> 為 true 時從馬的正面拍閘門，
        /// 開跑後停 <paramref name="holdSeconds"/> 秒，再花 <paramref name="blendSeconds"/> 秒移到側面跟拍。
        /// </summary>
        public void SetStartShot(bool fromFront, float holdSeconds, float blendSeconds)
        {
            _startFromFront = fromFront;
            _startHoldSeconds = Mathf.Max(0f, holdSeconds);
            _startBlendSeconds = Mathf.Max(0.1f, blendSeconds);
        }

        /// <summary>起跑閘鏡位。待機與下注階段用。</summary>
        public void FrameGate()
        {
            _transitioning = false;
            if (_startFromFront)
            {
                _targetPosition = FrontPosition();
                _targetLookAt = FrontLookAt();
                return;
            }

            _targetPosition = new Vector3(TrackLayout.StartX - 9f, 5.5f, -(_laneSpan * 0.5f + TrackLayout.RailOffset + 11f));
            _targetLookAt = new Vector3(TrackLayout.StartX + 2f, 1.5f, 0f);
        }

        /// <summary>開跑時呼叫：正面鏡位下開始「停一下再轉到側拍」的轉場；側面鏡位則照常平滑追過去。</summary>
        public void BeginRaceTransition()
        {
            if (!_startFromFront)
            {
                return;
            }

            _transitioning = true;
            _transitionStartTime = Time.time;
        }

        private Vector3 FrontPosition()
        {
            return new Vector3(TrackLayout.StartX + FrontDistance, FrontHeight, -FrontSideOffset);
        }

        private static Vector3 FrontLookAt()
        {
            return new Vector3(TrackLayout.StartX + 0.5f, 1.5f, 0f);
        }

        /// <summary>
        /// 賽中側面跟拍。鏡頭對準領先者與最後一名的中點，並依馬群拉開的幅度自動拉遠。
        ///
        /// 會需要動態拉遠是因為體力驅動：有人猛搖時那匹馬會大幅甩開其他馬，
        /// 固定鏡位下後段班會整個被擠出畫面，看起來就不像在比賽了。
        /// </summary>
        public void FollowPack(float leaderProgress01, float trailerProgress01)
        {
            float leaderX = TrackLayout.ProgressToX(leaderProgress01);
            float trailerX = TrackLayout.ProgressToX(trailerProgress01);

            float midX = (leaderX + trailerX) * 0.5f;
            float spread = Mathf.Max(0f, leaderX - trailerX);

            // 視野寬度與鏡頭距離成正比，所以要納入的間距越大、就要退得越遠
            float distance = Mathf.Clamp(
                BaseFollowDistance + spread * SpreadZoomFactor,
                BaseFollowDistance, MaxFollowDistance);

            // 退遠時同步升高，維持俯角，不然遠處會被欄杆與看台擋住
            float height = Mathf.Lerp(BaseFollowHeight, MaxFollowHeight,
                Mathf.InverseLerp(BaseFollowDistance, MaxFollowDistance, distance));

            _targetPosition = new Vector3(
                midX, height, -(_laneSpan * 0.5f + TrackLayout.RailOffset + distance));
            _targetLookAt = new Vector3(midX, 1.6f, 0f);
        }

        private const float BaseFollowDistance = 18f;
        private const float MaxFollowDistance = 46f;
        private const float BaseFollowHeight = 7f;

        // 退遠時只微幅升高。升太多會把俯角拉大，下半個畫面就整片變成空草地
        private const float MaxFollowHeight = 9.5f;

        /// <summary>每單位間距要多退多遠。水平視野約為距離的 1.36 倍，抓 0.85 留有餘裕。</summary>
        private const float SpreadZoomFactor = 0.85f;

        /// <summary>衝線特寫。從終點前方斜看回來。</summary>
        public void FramePhotoFinish()
        {
            _transitioning = false;
            _targetPosition = new Vector3(TrackLayout.FinishX + 15f, 4.8f, -(_laneSpan * 0.5f + TrackLayout.RailOffset + 11f));
            _targetLookAt = new Vector3(TrackLayout.FinishX - 4f, 1.6f, 0f);
        }

        /// <summary>立刻移到目標鏡位，不做平滑。切換場次時用。</summary>
        public void SnapToTarget()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.transform.position = _targetPosition;
            _camera.transform.LookAt(_targetLookAt);
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            if (_transitioning)
            {
                UpdateRaceTransition();
                return;
            }

            float blend = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            Transform cameraTransform = _camera.transform;

            cameraTransform.position = Vector3.Lerp(cameraTransform.position, _targetPosition, blend);

            Quaternion desired = Quaternion.LookRotation(_targetLookAt - cameraTransform.position);
            cameraTransform.rotation = Quaternion.Slerp(
                cameraTransform.rotation, desired, 1f - Mathf.Exp(-SnapRate * Time.deltaTime));
        }

        /// <summary>
        /// 正面 → 側拍的轉場。直接算出每一幀的位置（不再疊加指數平滑），
        /// 側拍的目標由 <see cref="FollowPack"/> 每幀更新，所以轉場結束時剛好接上跟拍，不會跳一下。
        /// </summary>
        private void UpdateRaceTransition()
        {
            float elapsed = Time.time - _transitionStartTime;
            float progress = Mathf.Clamp01((elapsed - _startHoldSeconds) / _startBlendSeconds);
            float mainT = Mathf.SmoothStep(0f, 1f, progress);
            float sideT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * SideLeadFactor));

            Vector3 from = FrontPosition();
            Vector3 position = new Vector3(
                Mathf.Lerp(from.x, _targetPosition.x, mainT),
                Mathf.Lerp(from.y, _targetPosition.y, mainT),
                Mathf.Lerp(from.z, _targetPosition.z, sideT));
            Vector3 lookAt = Vector3.Lerp(FrontLookAt(), _targetLookAt, mainT);

            Transform cameraTransform = _camera.transform;
            cameraTransform.position = position;
            cameraTransform.rotation = Quaternion.LookRotation(lookAt - position);

            if (progress >= 1f)
            {
                _transitioning = false;
            }
        }
    }
}
