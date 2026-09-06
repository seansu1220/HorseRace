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

        /// <summary>賽中側面跟拍。</summary>
        public void FollowPack(float leaderProgress01, float packProgress01)
        {
            // 偏向領先者但不完全跟著他，否則被拉開的最後一匹會整個出鏡
            float focus = Mathf.Lerp(packProgress01, leaderProgress01, 0.55f);
            float focusX = TrackLayout.ProgressToX(focus);

            // 前瞻量刻意壓小：往前帶太多會把馬群推到畫面左下角，重點反而變成空賽道
            _targetPosition = new Vector3(focusX - 3f, 7f, -(_laneSpan * 0.5f + TrackLayout.RailOffset + 18f));
            _targetLookAt = new Vector3(focusX + 2f, 1.6f, 0f);
        }

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
