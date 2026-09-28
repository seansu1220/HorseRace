using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 把賽道包裝成「城市街道賽」：賽道是一條大馬路，遠側（+Z）一整排大樓、後面是摩天樓天際線，
    /// 人行道上有路燈與行道樹，每隔一段留一個十字路口。
    ///
    /// 模型來自 Kenney City Kit（CC0），放在 Resources/Scenery/Kenney/。任何一個模型缺檔只會少那一個物件，
    /// 不會讓場景建不起來。鏡頭一律在近側（−Z）低角度側拍，所以近側只鋪平面的人行道，
    /// 不放任何會擋住馬的高物件。
    /// </summary>
    public static class CityScenery
    {
        private const string KitRoot = "Scenery/Kenney/";

        private static readonly string[] StreetBuildings =
        {
            "Commercial/building-a", "Commercial/building-b", "Commercial/building-c", "Commercial/building-d",
            "Commercial/building-e", "Commercial/building-f", "Commercial/building-g", "Commercial/building-h",
            "Commercial/building-i", "Commercial/building-j", "Commercial/building-k", "Commercial/building-l",
            "Commercial/building-m", "Commercial/building-n"
        };

        private static readonly string[] SkylineBuildings =
        {
            "Commercial/building-skyscraper-a", "Commercial/building-skyscraper-b", "Commercial/building-skyscraper-c",
            "Commercial/building-skyscraper-d", "Commercial/building-skyscraper-e", "Commercial/building-l",
            "Commercial/building-m", "Commercial/building-n"
        };

        private static readonly string[] Trees = { "Suburban/tree-large", "Suburban/tree-small" };
        private const string StreetLamp = "Roads/light-curved";
        private const string TrafficLight = "Roads/traffic-light";

        private static readonly Color BlockColor = new Color(0.50f, 0.52f, 0.55f);
        private static readonly Color StreetColor = new Color(0.33f, 0.34f, 0.38f);
        private static readonly Color SidewalkColor = new Color(0.64f, 0.65f, 0.68f);
        private static readonly Color PavingJointColor = new Color(0.52f, 0.53f, 0.56f);
        private static readonly Color CurbColor = new Color(0.88f, 0.88f, 0.90f);
        private static readonly Color CrosswalkColor = new Color(0.93f, 0.93f, 0.90f);

        // ---- 版面（世界單位）。賽道寬約 13、長 160，街道往兩端各延伸一段讓起跑與衝線鏡頭有背景 ----
        private const float StreetBeforeStart = 110f;
        private const float StreetAfterFinish = 130f;
        private const float SidewalkGap = 0.6f;
        private const float SidewalkWidth = 6f;
        private const float CurbHeight = 0.25f;
        private const float BuildingSetback = 0.8f;
        private const float CrossStreetWidth = 16f;
        private const float CrossStreetDepth = 260f;
        private const float SkylineRowOffset = 34f;
        private const float FarSkylineRowOffset = 80f;
        private const float LampCurbInset = 0.9f;
        private const float TreeCurbInset = 4.6f;

        /// <summary>人行道地磚接縫的間距。近側人行道是鏡頭前的一大片平面，有接縫才不會像一塊死板的灰，跟拍時也有速度感。</summary>
        private const float PavingJointSpacing = 4f;

        private static readonly HashSet<string> ReportedMissing = new HashSet<string>();

        /// <summary>在 <paramref name="parent"/> 下建立整條街。</summary>
        public static void Build(Transform parent, int laneCount, SceneryConfig config)
        {
            GameObject root = new GameObject("CityStreet");
            root.transform.SetParent(parent, false);

            StreetPlan plan = new StreetPlan(laneCount, config);
            System.Random random = new System.Random(config.Seed);

            BuildGround(root.transform, plan);
            BuildSidewalks(root.transform, plan);
            BuildCrossStreets(root.transform, plan);
            BuildBuildingRow(root.transform, plan, random, StreetBuildings, plan.BuildingFrontZ, 1f, 0.6f, 2.2f);
            BuildBuildingRow(root.transform, plan, random, SkylineBuildings,
                plan.BuildingFrontZ + SkylineRowOffset, 1.15f, 3f, 9f);
            BuildBuildingRow(root.transform, plan, random, SkylineBuildings,
                plan.BuildingFrontZ + FarSkylineRowOffset, 1.5f, 6f, 16f);
            BuildStreetFurniture(root.transform, plan, random);
        }

        /// <summary>整條街的尺寸，由賽道寬度與設定推導，各個建置步驟共用。</summary>
        private sealed class StreetPlan
        {
            public readonly SceneryConfig Config;
            public readonly float Scale;
            public readonly float StartX;
            public readonly float EndX;
            public readonly float SidewalkInnerZ;
            public readonly float SidewalkOuterZ;
            public readonly float BuildingFrontZ;
            public readonly List<float> CrossStreetXs = new List<float>();

            public StreetPlan(int laneCount, SceneryConfig config)
            {
                Config = config;
                Scale = (float)config.KitScale;
                StartX = TrackLayout.StartX - StreetBeforeStart;
                EndX = TrackLayout.FinishX + StreetAfterFinish;
                SidewalkInnerZ = TrackLayout.LaneSpan(laneCount) * 0.5f + TrackLayout.RailOffset + SidewalkGap;
                SidewalkOuterZ = SidewalkInnerZ + SidewalkWidth;
                BuildingFrontZ = SidewalkOuterZ + BuildingSetback;

                float spacing = (float)config.CrossStreetSpacing;
                if (spacing > 0f)
                {
                    // 路口落在 spacing 的半格上（35、105、175…），不會剛好壓在起跑線或終點線
                    for (float x = spacing * 0.5f; x < EndX - CrossStreetWidth; x += spacing)
                    {
                        CrossStreetXs.Add(x);
                    }

                    for (float x = -spacing * 0.5f; x > StartX + CrossStreetWidth; x -= spacing)
                    {
                        CrossStreetXs.Add(x);
                    }
                }
            }

            public float Length
            {
                get { return EndX - StartX; }
            }

            public float CenterX
            {
                get { return (StartX + EndX) * 0.5f; }
            }

            /// <summary>x 是否落在某個十字路口的範圍內（含左右 <paramref name="margin"/>）。</summary>
            public bool InCrossStreet(float x, float margin)
            {
                foreach (float crossX in CrossStreetXs)
                {
                    if (Mathf.Abs(x - crossX) < CrossStreetWidth * 0.5f + margin)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // ---- 地面、人行道、路口 ----

        private static void BuildGround(Transform parent, StreetPlan plan)
        {
            // 街廓地面（大樓腳下），比馬路亮一點，才分得出哪裡是路
            GameObject block = TrackBuilder.CreatePrimitive(PrimitiveType.Plane, "CityBlocks", parent, BlockColor);
            block.transform.localPosition = new Vector3(plan.CenterX, -0.02f, 0f);
            block.transform.localScale = new Vector3((plan.Length + 400f) / 10f, 1f, 600f / 10f);

            // 賽道所在的大馬路：兩側人行道之間全是柏油
            float width = plan.SidewalkInnerZ * 2f;
            GameObject street = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Street", parent, StreetColor);
            street.transform.localPosition = new Vector3(plan.CenterX, 0.02f, 0f);
            street.transform.localScale = new Vector3(plan.Length + 400f, 0.04f, width);
        }

        private static void BuildSidewalks(Transform parent, StreetPlan plan)
        {
            // 人行道在路口處斷開；兩側都鋪（近側只有平面，不擋鏡頭）
            List<float> cuts = new List<float>(plan.CrossStreetXs);
            cuts.Sort();
            float segmentStart = plan.StartX - 200f;
            foreach (float crossX in cuts)
            {
                AddSidewalkPair(parent, plan, segmentStart, crossX - CrossStreetWidth * 0.5f);
                segmentStart = crossX + CrossStreetWidth * 0.5f;
            }

            AddSidewalkPair(parent, plan, segmentStart, plan.EndX + 200f);
        }

        private static void AddSidewalkPair(Transform parent, StreetPlan plan, float fromX, float toX)
        {
            if (toX - fromX < 0.5f)
            {
                return;
            }

            float centerZ = (plan.SidewalkInnerZ + plan.SidewalkOuterZ) * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject walk = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Sidewalk", parent, SidewalkColor);
                walk.transform.localPosition = new Vector3((fromX + toX) * 0.5f, CurbHeight * 0.5f, side * centerZ);
                walk.transform.localScale = new Vector3(toX - fromX, CurbHeight, SidewalkWidth);

                // 路緣石：人行道靠馬路那一側的亮色邊，遠看才有「路」的輪廓
                GameObject curb = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Curb", parent, CurbColor);
                curb.transform.localPosition = new Vector3(
                    (fromX + toX) * 0.5f, CurbHeight * 0.5f + 0.01f, side * (plan.SidewalkInnerZ + 0.15f));
                curb.transform.localScale = new Vector3(toX - fromX, CurbHeight + 0.02f, 0.3f);

                AddPavingJoints(parent, plan, fromX, toX, side * centerZ);
            }
        }

        private static void AddPavingJoints(Transform parent, StreetPlan plan, float fromX, float toX, float centerZ)
        {
            // 只鋪鏡頭看得到的範圍；人行道兩端為了遮住地平線延伸得很遠，那裡不需要接縫
            float start = Mathf.Max(fromX, plan.StartX);
            float end = Mathf.Min(toX, plan.EndX);
            for (float x = Mathf.Ceil(start / PavingJointSpacing) * PavingJointSpacing; x < end; x += PavingJointSpacing)
            {
                GameObject joint = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "PavingJoint", parent, PavingJointColor);
                joint.transform.localPosition = new Vector3(x, CurbHeight + 0.005f, centerZ);
                joint.transform.localScale = new Vector3(0.12f, 0.01f, SidewalkWidth);
            }
        }

        private static void BuildCrossStreets(Transform parent, StreetPlan plan)
        {
            foreach (float crossX in plan.CrossStreetXs)
            {
                // 往遠處延伸的橫向馬路，只做遠側（近側在鏡頭後方）
                GameObject road = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "CrossStreet", parent, StreetColor);
                road.transform.localPosition = new Vector3(crossX, 0.02f, plan.SidewalkInnerZ + CrossStreetDepth * 0.5f);
                road.transform.localScale = new Vector3(CrossStreetWidth, 0.04f, CrossStreetDepth);

                AddCrosswalk(parent, crossX, (plan.SidewalkInnerZ + plan.SidewalkOuterZ) * 0.5f);
                AddCrosswalk(parent, crossX, -(plan.SidewalkInnerZ + plan.SidewalkOuterZ) * 0.5f);
            }
        }

        /// <summary>斑馬線：路口處橫跨人行道缺口的白色條紋。</summary>
        private static void AddCrosswalk(Transform parent, float crossX, float centerZ)
        {
            const int stripes = 7;
            float pitch = CrossStreetWidth / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GameObject stripe = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Crosswalk", parent, CrosswalkColor);
                stripe.transform.localPosition = new Vector3(
                    crossX - CrossStreetWidth * 0.5f + pitch * (i + 0.5f), 0.05f, centerZ);
                stripe.transform.localScale = new Vector3(pitch * 0.55f, 0.02f, SidewalkWidth - 0.6f);
            }
        }

        // ---- 大樓 ----

        /// <summary>
        /// 沿 X 軸從街頭排到街尾，一棟接一棟，正面對齊 <paramref name="frontZ"/>。
        /// 路口的位置空下來，讓橫向馬路看得到縱深。
        /// </summary>
        private static void BuildBuildingRow(Transform parent, StreetPlan plan, System.Random random,
                                             string[] models, float frontZ, float scaleFactor,
                                             float minGap, float maxGap)
        {
            float extra = frontZ - plan.BuildingFrontZ; // 越後排越寬，鏡頭斜看時兩端才不會露出空地
            float cursor = plan.StartX - extra;
            float endX = plan.EndX + extra;
            int guard = 0;

            while (cursor < endX && guard++ < 400)
            {
                if (plan.InCrossStreet(cursor, 1f))
                {
                    cursor += 2f;
                    continue;
                }

                string model = models[random.Next(models.Length)];
                GameObject building = Spawn(parent, model, plan.Scale * scaleFactor,
                    (float)plan.Config.BuildingYawDegrees);
                if (building == null)
                {
                    cursor += 10f;
                    continue;
                }

                Bounds bounds = CombinedBounds(building);
                float width = bounds.size.x;
                if (plan.InCrossStreet(cursor + width, 1f) || plan.InCrossStreet(cursor + width * 0.5f, 1f))
                {
                    // 這棟會蓋到路口上，改放下一個位置
                    Object.Destroy(building);
                    cursor += 2f;
                    continue;
                }

                building.transform.position += new Vector3(cursor - bounds.min.x, -bounds.min.y, frontZ - bounds.min.z);
                cursor += width + minGap + (float)random.NextDouble() * (maxGap - minGap);
            }
        }

        // ---- 路燈、行道樹、紅綠燈 ----

        private static void BuildStreetFurniture(Transform parent, StreetPlan plan, System.Random random)
        {
            float spacing = (float)plan.Config.LampSpacing;
            float lampZ = plan.SidewalkInnerZ + LampCurbInset;
            float treeZ = plan.SidewalkInnerZ + TreeCurbInset;

            for (float x = plan.StartX; x < plan.EndX; x += spacing)
            {
                if (!plan.InCrossStreet(x, 2f))
                {
                    // 路燈的燈臂在模型裡朝 −Z，正好伸向馬路
                    PlaceOnSidewalk(parent, StreetLamp, plan.Scale, 0f, new Vector3(x, CurbHeight, lampZ));
                }

                float treeX = x + spacing * 0.5f;
                if (!plan.InCrossStreet(treeX, 3f))
                {
                    string tree = Trees[random.Next(Trees.Length)];
                    float yaw = (float)random.NextDouble() * 360f;
                    PlaceOnSidewalk(parent, tree, plan.Scale, yaw, new Vector3(treeX, CurbHeight, treeZ));
                }
            }

            // 每個路口兩角各一支紅綠燈，面向馬路
            foreach (float crossX in plan.CrossStreetXs)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = crossX + side * (CrossStreetWidth * 0.5f + 0.8f);
                    PlaceOnSidewalk(parent, TrafficLight, plan.Scale, 180f, new Vector3(x, CurbHeight, lampZ));
                }
            }
        }

        private static void PlaceOnSidewalk(Transform parent, string model, float scale, float yaw, Vector3 position)
        {
            GameObject instance = Spawn(parent, model, scale, yaw);
            if (instance != null)
            {
                instance.transform.position += position;
            }
        }

        // ---- 共用 ----

        /// <summary>在原點生成模型（已縮放、旋轉），並關掉投影：大樓的影子會蓋住整條賽道。</summary>
        private static GameObject Spawn(Transform parent, string model, float scale, float yaw)
        {
            GameObject prefab = Resources.Load<GameObject>(KitRoot + model);
            if (prefab == null)
            {
                if (ReportedMissing.Add(model))
                {
                    Debug.LogWarning("[CityScenery] 找不到模型 Resources/" + KitRoot + model + "，略過。");
                }

                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
            instance.transform.localScale = prefab.transform.localScale * scale;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return instance;
        }

        private static Bounds CombinedBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(instance.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
