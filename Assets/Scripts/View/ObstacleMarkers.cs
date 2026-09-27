using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 賽道上的障礙物（障礙券放下的柵欄）。同一條跑道可以同時有好幾個，
    /// 每條跑道各有一組重複使用的柵欄物件，數量不夠時才加建。
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

        private Transform _track;
        private int _laneCount;
        private List<GameObject>[] _pools;

        public static ObstacleMarkers Build(Transform track, int laneCount)
        {
            ObstacleMarkers markers = new ObstacleMarkers
            {
                _track = track,
                _laneCount = laneCount,
                _pools = new List<GameObject>[laneCount]
            };

            for (int lane = 0; lane < laneCount; lane++)
            {
                markers._pools[lane] = new List<GameObject>();
            }

            return markers;
        }

        /// <summary>依賽況顯示每條跑道上的所有障礙物，多出來的柵欄隱藏。</summary>
        public void Refresh(RaceEngine race)
        {
            for (int lane = 0; lane < _laneCount; lane++)
            {
                List<ObstacleMark> obstacles = race != null && lane < race.HorseCount
                    ? race.Horses[lane].Obstacles
                    : null;
                int count = obstacles == null ? 0 : obstacles.Count;
                List<GameObject> pool = _pools[lane];

                while (pool.Count < count)
                {
                    pool.Add(BuildBarrier(_track, lane, _laneCount));
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    bool active = i < count;
                    if (pool[i].activeSelf != active)
                    {
                        pool[i].SetActive(active);
                    }

                    if (active)
                    {
                        Place(pool[i], obstacles[i].Position / race.TrackLengthMeters);
                    }
                }
            }
        }

        public void HideAll()
        {
            foreach (List<GameObject> pool in _pools)
            {
                foreach (GameObject marker in pool)
                {
                    if (marker.activeSelf)
                    {
                        marker.SetActive(false);
                    }
                }
            }
        }

        private static void Place(GameObject marker, double progress)
        {
            Vector3 position = marker.transform.localPosition;
            position.x = TrackLayout.ProgressToX((float)progress) + NoseOffset;
            marker.transform.localPosition = position;
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
