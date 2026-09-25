using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 賽道上的障礙物（障礙券放下的柵欄）。每條跑道一個，前方有障礙物時才顯示。
    /// 位置由 Core 的公尺換算成賽程完成度，再換成世界座標，與馬匹同一套換算。
    /// </summary>
    public sealed class ObstacleMarkers
    {
        /// <summary>
        /// 往前挪一點：馬匹座標是身體中心，鼻尖在前方約 1.9 個單位。
        /// 不挪的話，馬停下時柵欄會插在馬身體裡。
        /// </summary>
        private const float NoseOffset = 2.1f;

        private static readonly Color BarrierColor = new Color(0.91f, 0.34f, 0.49f);
        private static readonly Color StripeColor = new Color(0.97f, 0.96f, 0.92f);

        private GameObject[] _markers;

        public static ObstacleMarkers Build(Transform track, int laneCount)
        {
            ObstacleMarkers markers = new ObstacleMarkers { _markers = new GameObject[laneCount] };
            for (int lane = 0; lane < laneCount; lane++)
            {
                markers._markers[lane] = BuildBarrier(track, lane, laneCount);
            }

            return markers;
        }

        /// <summary>依賽況顯示或隱藏每條跑道的障礙物。</summary>
        public void Refresh(RaceEngine race)
        {
            for (int lane = 0; lane < _markers.Length; lane++)
            {
                bool active = race != null && lane < race.HorseCount
                              && race.Horses[lane].ObstacleAt != HorseState.NoObstacle;
                GameObject marker = _markers[lane];

                if (marker.activeSelf != active)
                {
                    marker.SetActive(active);
                }

                if (!active)
                {
                    continue;
                }

                float progress = (float)(race.Horses[lane].ObstacleAt / race.TrackLengthMeters);
                Vector3 position = marker.transform.localPosition;
                position.x = TrackLayout.ProgressToX(progress) + NoseOffset;
                marker.transform.localPosition = position;
            }
        }

        public void HideAll()
        {
            foreach (GameObject marker in _markers)
            {
                marker.SetActive(false);
            }
        }

        private static GameObject BuildBarrier(Transform track, int lane, int laneCount)
        {
            GameObject root = new GameObject("Obstacle_" + lane);
            root.transform.SetParent(track, false);
            root.transform.localPosition = new Vector3(0f, TrackLayout.GroundY, TrackLayout.LaneZ(lane, laneCount));

            float width = TrackLayout.LaneWidth * 0.8f;

            GameObject board = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Board", root.transform, BarrierColor, true);
            board.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            board.transform.localScale = new Vector3(0.3f, 0.7f, width);

            GameObject stripe = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Stripe", root.transform, StripeColor, true);
            stripe.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            stripe.transform.localScale = new Vector3(0.32f, 0.2f, width * 1.01f);

            // 兩支腳：看得出是立在跑道上的柵欄，而不是浮在空中的方塊
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject leg = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Leg", root.transform, StripeColor);
                leg.transform.localPosition = new Vector3(0f, 0.2f, side * width * 0.4f);
                leg.transform.localScale = new Vector3(0.16f, 0.4f, 0.16f);
            }

            root.SetActive(false);
            return root;
        }
    }
}
