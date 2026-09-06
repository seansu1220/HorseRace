using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 攝影機運鏡。三種鏡位：起跑閘全景、賽中側面跟拍、衝線特寫。
    /// 全部以平滑逼近的方式移動，避免大螢幕上的畫面突兀跳動。
    /// </summary>
    public sealed class RaceCameraRig : MonoBehaviour
    {
        private const float FollowRate = 3.2f;
        private const float SnapRate = 40f;

        private Camera _camera;
        private float _laneSpan;

        private Vector3 _targetPosition;
        private Vector3 _targetLookAt;

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

        /// <summary>起跑閘全景。待機與下注階段用。</summary>
        public void FrameGate()
        {
            _targetPosition = new Vector3(TrackLayout.StartX - 9f, 5.5f, -(_laneSpan * 0.5f + TrackLayout.RailOffset + 11f));
            _targetLookAt = new Vector3(TrackLayout.StartX + 2f, 1.5f, 0f);
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

            float blend = 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
            Transform cameraTransform = _camera.transform;

            cameraTransform.position = Vector3.Lerp(cameraTransform.position, _targetPosition, blend);

            Quaternion desired = Quaternion.LookRotation(_targetLookAt - cameraTransform.position);
            cameraTransform.rotation = Quaternion.Slerp(
                cameraTransform.rotation, desired, 1f - Mathf.Exp(-SnapRate * Time.deltaTime));
        }
    }
}
