using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 起跑閘門：綠色鋼架、每匹馬一格，上方橫梁掛白色號碼板。馬匹開跑前就站在格子裡。
    ///
    /// 隔間只用兩根細橫桿，不做實心隔板：鏡頭從側面拍，實心隔板會把遠側跑道的馬整匹擋住。
    /// </summary>
    public static class StartingGate
    {
        private static readonly Color FrameColor = new Color(0.12f, 0.45f, 0.26f);
        private static readonly Color PanelColor = new Color(0.96f, 0.96f, 0.94f);
        private static readonly Color WheelColor = new Color(0.12f, 0.12f, 0.14f);

        /// <summary>閘門後緣與前緣（相對起跑線）。馬身長 2.4、頭往前伸，前緣留在馬頭前方一點。</summary>
        private const float BackOffset = -2.0f;
        private const float FrontOffset = 1.7f;
        private const float Height = 3.4f;
        private const float BarThickness = 0.14f;

        public static void Build(Transform parent, int laneCount)
        {
            GameObject root = new GameObject("StartingGate");
            root.transform.SetParent(parent, false);
            Transform node = root.transform;

            float span = TrackLayout.LaneSpan(laneCount);
            float backX = TrackLayout.StartX + BackOffset;
            float frontX = TrackLayout.StartX + FrontOffset;
            float centerX = (backX + frontX) * 0.5f;

            for (int boundary = 0; boundary <= laneCount; boundary++)
            {
                float z = -span * 0.5f + boundary * TrackLayout.LaneWidth;
                BuildPartition(node, backX, frontX, z);
            }

            // 前後上橫梁與頂上的走道
            foreach (float x in new[] { backX, frontX })
            {
                Bar(node, "TopBeam", new Vector3(x, Height, 0f), new Vector3(BarThickness * 1.5f, 0.3f, span + 0.4f));
            }

            // 工作人員走道只在後側一條，整片蓋住的話從上往下拍會把騎師遮掉
            Bar(node, "Walkway", new Vector3(backX + 0.4f, Height + 0.2f, 0f), new Vector3(0.8f, 0.1f, span + 0.4f));

            // 每格上方一塊白色號碼板（面向鏡頭側的前緣）
            for (int lane = 0; lane < laneCount; lane++)
            {
                GameObject panel = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "StallPanel", node, PanelColor);
                panel.transform.localPosition = new Vector3(frontX + 0.12f, Height - 0.35f,
                    TrackLayout.LaneZ(lane, laneCount));
                panel.transform.localScale = new Vector3(0.06f, 0.5f, TrackLayout.LaneWidth - 0.5f);
            }

            // 外側兩端的輪子：閘門是拖進場的，這是它最好認的特徵
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject wheel = TrackBuilder.CreatePrimitive(PrimitiveType.Cylinder, "Wheel", node, WheelColor);
                wheel.transform.localPosition = new Vector3(centerX, 0.55f, side * (span * 0.5f + 0.35f));
                wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                wheel.transform.localScale = new Vector3(1.1f, 0.12f, 1.1f);
            }
        }

        /// <summary>一道隔間：前後兩根立柱，中間兩根細橫桿。</summary>
        private static void BuildPartition(Transform parent, float backX, float frontX, float z)
        {
            float depth = frontX - backX;
            float centerX = (backX + frontX) * 0.5f;

            foreach (float x in new[] { backX, frontX })
            {
                Bar(parent, "Post", new Vector3(x, Height * 0.5f, z), new Vector3(BarThickness, Height, BarThickness));
            }

            Bar(parent, "RailLow", new Vector3(centerX, 1.0f, z), new Vector3(depth, 0.08f, 0.08f));
            Bar(parent, "RailHigh", new Vector3(centerX, 2.0f, z), new Vector3(depth, 0.08f, 0.08f));
        }

        private static void Bar(Transform parent, string name, Vector3 position, Vector3 size)
        {
            GameObject bar = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, name, parent, FrameColor);
            bar.transform.localPosition = position;
            bar.transform.localScale = size;
        }
    }
}
