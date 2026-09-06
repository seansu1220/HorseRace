using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 用 Unity 內建的基本幾何體搭出賽馬場。素材全部程式生成，沒有任何外部依賴，
    /// 使用者提供正式素材後只要改這個檔案即可，不會動到 Core 或 Net。
    /// </summary>
    public static class TrackBuilder
    {
        private static readonly Color GrassColor = new Color(0.22f, 0.42f, 0.20f);
        private static readonly Color TrackColor = new Color(0.62f, 0.44f, 0.29f);
        private static readonly Color LineColor = new Color(0.94f, 0.94f, 0.90f);
        private static readonly Color RailColor = new Color(0.88f, 0.88f, 0.92f);
        private static readonly Color StandColor = new Color(0.34f, 0.36f, 0.44f);

        /// <summary>建立整座賽馬場，回傳其根物件。</summary>
        public static Transform Build(int laneCount)
        {
            GameObject root = new GameObject("Racetrack");

            float span = TrackLayout.LaneSpan(laneCount);
            float length = TrackLayout.VisualLength;
            float centerX = length * 0.5f;

            BuildGrass(root.transform, centerX, length, span);
            BuildTrackBed(root.transform, centerX, length, span);
            BuildLaneDividers(root.transform, laneCount, centerX, length);
            BuildStartLine(root.transform, span);
            BuildFinishLine(root.transform, laneCount);
            BuildRails(root.transform, centerX, length, span);
            BuildDistanceMarkers(root.transform, span);
            BuildGrandstand(root.transform, centerX, length, span);

            return root.transform;
        }

        private static void BuildGrass(Transform parent, float centerX, float length, float span)
        {
            // Plane 的預設邊長是 10，所以 scale 要除以 10
            GameObject grass = CreatePrimitive(PrimitiveType.Plane, "Grass", parent, GrassColor);
            grass.transform.localPosition = new Vector3(centerX, 0f, 0f);
            grass.transform.localScale = new Vector3((length + 80f) / 10f, 1f, (span + 90f) / 10f);
        }

        private static void BuildTrackBed(Transform parent, float centerX, float length, float span)
        {
            GameObject bed = CreatePrimitive(PrimitiveType.Cube, "TrackBed", parent, TrackColor);
            bed.transform.localPosition = new Vector3(centerX, TrackLayout.BedHeight * 0.5f, 0f);
            bed.transform.localScale = new Vector3(length + 24f, TrackLayout.BedHeight, span);
        }

        private static void BuildLaneDividers(Transform parent, int laneCount, float centerX, float length)
        {
            // 只畫跑道「之間」的分隔線，外側邊界交給欄杆表現
            for (int lane = 0; lane < laneCount - 1; lane++)
            {
                float z = TrackLayout.LaneZ(lane, laneCount) + TrackLayout.LaneWidth * 0.5f;
                GameObject line = CreatePrimitive(PrimitiveType.Cube, "LaneLine_" + lane, parent, LineColor);
                line.transform.localPosition = new Vector3(centerX, TrackLayout.GroundY + 0.01f, z);
                line.transform.localScale = new Vector3(length + 24f, 0.02f, 0.12f);
            }
        }

        private static void BuildStartLine(Transform parent, float span)
        {
            GameObject line = CreatePrimitive(PrimitiveType.Cube, "StartLine", parent, LineColor);
            line.transform.localPosition =
                new Vector3(TrackLayout.StartX, TrackLayout.GroundY + 0.02f, 0f);
            line.transform.localScale = new Vector3(0.5f, 0.03f, span);
        }

        private static void BuildFinishLine(Transform parent, int laneCount)
        {
            GameObject group = new GameObject("FinishLine");
            group.transform.SetParent(parent, false);

            // 用交錯的黑白小方塊拼出格紋，遠看就是終點線
            const int rows = 2;
            int columns = Mathf.Max(8, laneCount * 6);
            float span = TrackLayout.LaneSpan(laneCount);
            float cellZ = span / columns;
            const float cellX = 0.45f;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    bool isWhite = (row + column) % 2 == 0;
                    Color color = isWhite ? LineColor : new Color(0.12f, 0.12f, 0.14f);

                    GameObject cell = CreatePrimitive(PrimitiveType.Cube, "Cell", group.transform, color);
                    cell.transform.localPosition = new Vector3(
                        TrackLayout.FinishX + (row - (rows - 1) * 0.5f) * cellX,
                        TrackLayout.GroundY + 0.02f,
                        -span * 0.5f + cellZ * (column + 0.5f));
                    cell.transform.localScale = new Vector3(cellX, 0.03f, cellZ);
                }
            }

            // 終點門柱，讓衝線那一刻在畫面上有明確的參照物
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject post = CreatePrimitive(PrimitiveType.Cube, "FinishPost", group.transform, RailColor);
                post.transform.localPosition = new Vector3(
                    TrackLayout.FinishX, 2.6f, side * (span * 0.5f + 0.6f));
                post.transform.localScale = new Vector3(0.4f, 5.2f, 0.4f);
            }

            GameObject beam = CreatePrimitive(PrimitiveType.Cube, "FinishBeam", group.transform, RailColor);
            beam.transform.localPosition = new Vector3(TrackLayout.FinishX, 5.2f, 0f);
            beam.transform.localScale = new Vector3(0.4f, 0.5f, span + 1.6f);
        }

        private static void BuildRails(Transform parent, float centerX, float length, float span)
        {
            // 欄杆要離跑道夠遠。貼著邊緣的話，從側面低角度看過去，
            // 近側欄杆的頂緣會與最近一匹馬的馬蹄幾乎共線，看起來像穿過馬腿。
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (span * 0.5f + TrackLayout.RailOffset);

                GameObject rail = CreatePrimitive(PrimitiveType.Cube, "Rail", parent, RailColor);
                rail.transform.localPosition = new Vector3(centerX, 0.75f, z);
                rail.transform.localScale = new Vector3(length + 24f, 0.12f, 0.12f);

                // 欄杆立柱，每 8 公尺一根，提供速度感的參照
                int postCount = Mathf.CeilToInt((length + 24f) / 8f);
                for (int i = 0; i <= postCount; i++)
                {
                    GameObject post = CreatePrimitive(PrimitiveType.Cube, "RailPost", parent, RailColor);
                    post.transform.localPosition = new Vector3(-12f + i * 8f, 0.4f, z);
                    post.transform.localScale = new Vector3(0.1f, 0.8f, 0.1f);
                }
            }
        }

        private static void BuildDistanceMarkers(Transform parent, float span)
        {
            // 四分之一、一半、四分之三處各插一支標竿，觀眾一眼看得出跑到哪
            float[] fractions = { 0.25f, 0.5f, 0.75f };
            for (int i = 0; i < fractions.Length; i++)
            {
                float x = TrackLayout.ProgressToX(fractions[i]);
                GameObject marker = CreatePrimitive(
                    PrimitiveType.Cube, "Marker_" + fractions[i], parent, new Color(0.95f, 0.75f, 0.20f));
                marker.transform.localPosition =
                    new Vector3(x, 1.1f, -(span * 0.5f + TrackLayout.RailOffset + 1.6f));
                marker.transform.localScale = new Vector3(0.16f, 2.2f, 0.16f);
            }
        }

        private static void BuildGrandstand(Transform parent, float centerX, float length, float span)
        {
            // 遠端的看台只是為了讓畫面有景深，不需要細節。
            //
            // 位置要退得夠遠：鏡頭是低角度側拍，看台只要靠近或加屋頂，
            // 從下方看到的就是一整片深色底面，會把整個上半畫面糊掉。
            GameObject stand = CreatePrimitive(PrimitiveType.Cube, "Grandstand", parent, StandColor);
            stand.transform.localPosition = new Vector3(centerX, 4f, span * 0.5f + 48f);
            stand.transform.localScale = new Vector3(length * 0.8f, 8f, 12f);

            // 頂緣壓一條亮色帶，讓它在天空前有輪廓而不是一塊死板的方形
            GameObject cap = CreatePrimitive(
                PrimitiveType.Cube, "GrandstandCap", parent, MaterialLibrary.Lighten(StandColor, 0.25f));
            cap.transform.localPosition = new Vector3(centerX, 8.3f, span * 0.5f + 48f);
            cap.transform.localScale = new Vector3(length * 0.8f, 0.6f, 12.6f);
        }

        /// <summary>
        /// 建立基本幾何體並套上顏色。一律移除 Collider——全場沒有任何物理需求，
        /// 留著只會白白增加場景的碰撞查詢成本。
        /// </summary>
        internal static GameObject CreatePrimitive(
            PrimitiveType type, string name, Transform parent, Color color, bool castShadows = false)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, false);

            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = MaterialLibrary.Opaque(color);
                // 只有馬匹投影。場景物件（尤其是巨大的看台）投影只會把賽道弄髒
                renderer.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return instance;
        }
    }
}
