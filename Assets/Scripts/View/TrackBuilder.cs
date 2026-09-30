using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 搭出賽道本身（路面、分道線、起跑閘門、終點、欄杆、距離標竿），再依設定包上周邊場景：
    /// 賽馬場（<see cref="RacecourseScenery"/>，預設）或城市街道（<see cref="CityScenery"/>）。
    /// 換素材只動 View，不會動到 Core 或 Net。
    /// </summary>
    public static class TrackBuilder
    {
        private static readonly Color AsphaltColor = new Color(0.27f, 0.28f, 0.32f);
        private static readonly Color TurfLight = new Color(0.42f, 0.70f, 0.31f);
        private static readonly Color TurfDark = new Color(0.36f, 0.62f, 0.26f);

        /// <summary>賽道草皮割草紋的寬度（世界單位）。</summary>
        private const float TurfStripeWidth = 6f;
        private static readonly Color LineColor = new Color(0.94f, 0.94f, 0.90f);
        private static readonly Color RailColor = new Color(0.88f, 0.88f, 0.92f);

        /// <summary>建立整座賽道與周邊場景，回傳其根物件。</summary>
        public static Transform Build(int laneCount, SceneryConfig scenery)
        {
            GameObject root = new GameObject("Racetrack");

            float span = TrackLayout.LaneSpan(laneCount);
            float length = TrackLayout.VisualLength;
            float centerX = length * 0.5f;
            bool city = scenery != null && scenery.CityStreet;

            if (city)
            {
                CityScenery.Build(root.transform, laneCount, scenery);
            }
            else
            {
                RacecourseScenery.Build(root.transform, laneCount, scenery ?? new SceneryConfig());
            }

            BuildTrackBed(root.transform, centerX, length, span, city);
            BuildLaneDividers(root.transform, laneCount, centerX, length);
            BuildStartLine(root.transform, span);
            StartingGate.Build(root.transform, laneCount);
            BuildFinishLine(root.transform, laneCount);
            BuildRails(root.transform, centerX, length, span);
            BuildDistanceMarkers(root.transform, span);

            return root.transform;
        }

        private static void BuildTrackBed(Transform parent, float centerX, float length, float span, bool city)
        {
            GameObject bed = CreatePrimitive(PrimitiveType.Cube, "TrackBed", parent, AsphaltColor);
            bed.transform.localPosition = new Vector3(centerX, TrackLayout.BedHeight * 0.5f, 0f);
            bed.transform.localScale = new Vector3(length + 24f, TrackLayout.BedHeight, span);

            if (!city)
            {
                // 草皮賽道：比外圍草地亮一點的割草紋，跟拍時一條條掠過就有速度感
                Texture2D stripes = ProceduralTextures.Stripes("TrackTurf", TurfLight, TurfDark);
                bed.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Textured(
                    stripes, new Vector2((length + 24f) / (TurfStripeWidth * 2f), 1f));
            }
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
