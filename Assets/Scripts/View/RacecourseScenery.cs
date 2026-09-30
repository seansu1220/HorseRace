using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 真正的賽馬場：割草紋草地、遠側（+Z）一道修剪整齊的樹籬與花壇、站位區的觀眾、
    /// 兩座有屋頂的階梯看台（坐滿觀眾）、會員會館、終點的勝利柱，後方一排樹林與遠山。
    ///
    /// 鏡頭一律在近側（−Z）低角度側拍，所以所有高物件都放在遠側，近側只有平坦的草地。
    /// 樹木與花來自 Kenney Nature Kit（CC0，Resources/Scenery/Kenney/Nature/），缺檔只會少那一棵；
    /// 其餘（看台、觀眾、樹籬、勝利柱）都是基本幾何體。
    /// </summary>
    public static class RacecourseScenery
    {
        private const string NatureRoot = "Scenery/Kenney/Nature/";

        private static readonly string[] TreeModels =
        {
            "tree_default", "tree_default_dark", "tree_oak", "tree_oak_dark", "tree_fat",
            "tree_detailed", "tree_detailed_dark", "tree_cone_dark"
        };

        private static readonly string[] FlowerModels = { "flower_redA", "flower_yellowA", "flower_purpleA" };

        private static readonly Color GrassLight = new Color(0.36f, 0.62f, 0.27f);
        private static readonly Color GrassDark = new Color(0.30f, 0.54f, 0.23f);
        private static readonly Color HedgeColor = new Color(0.14f, 0.36f, 0.16f);
        private static readonly Color ApronColor = new Color(0.80f, 0.77f, 0.70f);
        private static readonly Color ConcreteColor = new Color(0.74f, 0.74f, 0.76f);
        private static readonly Color SeatColor = new Color(0.16f, 0.40f, 0.26f);
        private static readonly Color RoofColor = new Color(0.93f, 0.93f, 0.92f);
        private static readonly Color FasciaColor = new Color(0.10f, 0.34f, 0.22f);
        private static readonly Color TrimColor = new Color(0.96f, 0.96f, 0.94f);
        private static readonly Color WallColor = new Color(0.90f, 0.86f, 0.76f);
        private static readonly Color WindowColor = new Color(0.22f, 0.32f, 0.42f);
        private static readonly Color HillColor = new Color(0.33f, 0.50f, 0.32f);
        private static readonly Color PostRed = new Color(0.84f, 0.16f, 0.16f);
        private static readonly Color BarkColor = new Color(0.40f, 0.29f, 0.20f);

        /// <summary>
        /// 樹葉改用自然的綠：Kenney 原色偏薄荷藍綠，放在草地旁邊很突兀。每棵樹隨機挑一種，樹林才有層次。
        /// </summary>
        private static readonly Color[] LeafColors =
        {
            new Color(0.25f, 0.48f, 0.20f), new Color(0.19f, 0.40f, 0.18f), new Color(0.31f, 0.53f, 0.22f)
        };

        private static readonly Color[] FlagColors =
        {
            new Color(0.86f, 0.22f, 0.22f), new Color(0.20f, 0.45f, 0.82f),
            new Color(0.95f, 0.78f, 0.22f), new Color(0.96f, 0.96f, 0.94f)
        };

        // ---- 版面（世界單位）。由遠側欄杆往外依序排開 ----
        private const float StripeWidth = 8f;
        private const float FlowerBedInset = 0.7f;
        private const float HedgeInset = 1.4f;
        private const float HedgeDepth = 1.0f;
        private const float HedgeHeight = 1.1f;
        private const float ApronDepth = 6f;
        private const float TierDepth = 1.3f;
        private const float TierRise = 0.55f;
        private const float StandBaseHeight = 1.6f;
        private const float SeatSpacing = 0.75f;
        private const float RoofClearance = 3.6f;
        private const float TreeSpacing = 7f;

        private const float SceneryBeforeStart = 170f;
        private const float SceneryAfterFinish = 190f;

        private static readonly HashSet<string> ReportedMissing = new HashSet<string>();

        /// <summary>看台的配置：沿 X 的範圍與階數。</summary>
        private struct StandSpec
        {
            public float FromX;
            public float ToX;
            public int Tiers;
        }

        public static void Build(Transform parent, int laneCount, SceneryConfig config)
        {
            GameObject root = new GameObject("Racecourse");
            root.transform.SetParent(parent, false);
            Transform node = root.transform;

            float railZ = TrackLayout.LaneSpan(laneCount) * 0.5f + TrackLayout.RailOffset;
            float hedgeZ = railZ + HedgeInset;
            float apronFrontZ = hedgeZ + HedgeDepth;
            float standFrontZ = apronFrontZ + ApronDepth;
            System.Random random = new System.Random(config.Seed);
            CrowdBuilder crowd = new CrowdBuilder(config.Seed);
            float density = (float)config.CrowdDensity;

            // 兩座看台：大看台在終點前後，小看台在前半段，中間留一段空隙看得到後面的樹
            StandSpec main = new StandSpec
            {
                FromX = TrackLayout.FinishX - 92f, ToX = TrackLayout.FinishX + 16f, Tiers = 10
            };
            StandSpec second = new StandSpec
            {
                FromX = TrackLayout.StartX - 14f, ToX = TrackLayout.FinishX - 102f, Tiers = 6
            };

            BuildGround(node);
            BuildHedgeAndFlowers(node, railZ, hedgeZ, random);
            BuildApron(node, apronFrontZ, standFrontZ, crowd, random, density);
            float mainBackZ = BuildStand(node, main, standFrontZ, crowd, random, density, true);
            BuildStand(node, second, standFrontZ, crowd, random, density, false);
            BuildClubhouse(node, TrackLayout.FinishX + 22f, standFrontZ);
            BuildWinningPost(node, railZ);
            crowd.Build(node, "Crowd");

            BuildTreeLine(node, mainBackZ + 6f, random);
            BuildHills(node, random);
        }

        // ---- 地面、樹籬、花壇 ----

        private static void BuildGround(Transform parent)
        {
            float length = SceneryBeforeStart + TrackLayout.VisualLength + SceneryAfterFinish + 400f;
            float centerX = (TrackLayout.StartX - SceneryBeforeStart + TrackLayout.FinishX + SceneryAfterFinish) * 0.5f;
            const float depth = 900f;

            // Plane 預設邊長 10；割草紋沿 X 交錯，跟拍時一條條掠過，速度感更明顯
            GameObject ground = TrackBuilder.CreatePrimitive(PrimitiveType.Plane, "Turf", parent, Color.white);
            ground.transform.localPosition = new Vector3(centerX, -0.01f, 0f);
            ground.transform.localScale = new Vector3(length / 10f, 1f, depth / 10f);
            Texture2D stripes = ProceduralTextures.Stripes("TurfStripes", GrassLight, GrassDark);
            ground.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.Textured(stripes, new Vector2(length / (StripeWidth * 2f), 1f));
        }

        private static void BuildHedgeAndFlowers(Transform parent, float railZ, float hedgeZ, System.Random random)
        {
            float fromX = TrackLayout.StartX - SceneryBeforeStart;
            float toX = TrackLayout.FinishX + SceneryAfterFinish;

            GameObject hedge = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Hedge", parent, HedgeColor);
            hedge.transform.localPosition = new Vector3((fromX + toX) * 0.5f, HedgeHeight * 0.5f, hedgeZ + HedgeDepth * 0.5f);
            hedge.transform.localScale = new Vector3(toX - fromX, HedgeHeight, HedgeDepth);

            // 樹籬前一排花，三朵一簇，只鋪在鏡頭會經過的範圍
            float flowerZ = railZ + FlowerBedInset;
            for (float x = TrackLayout.StartX - 40f; x < TrackLayout.FinishX + 40f; x += 3.2f)
            {
                string model = FlowerModels[random.Next(FlowerModels.Length)];
                for (int i = 0; i < 3; i++)
                {
                    float jitterX = (float)random.NextDouble() * 0.8f;
                    float jitterZ = (float)random.NextDouble() * 0.4f;
                    Place(parent, NatureRoot + model, 3f, (float)random.NextDouble() * 360f,
                        new Vector3(x + jitterX, 0f, flowerZ + jitterZ), null);
                }
            }
        }

        // ---- 站位區與看台 ----

        private static void BuildApron(Transform parent, float frontZ, float backZ, CrowdBuilder crowd,
                                       System.Random random, float density)
        {
            float fromX = TrackLayout.StartX - 30f;
            float toX = TrackLayout.FinishX + 50f;

            GameObject apron = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Apron", parent, ApronColor);
            apron.transform.localPosition = new Vector3((fromX + toX) * 0.5f, 0.03f, (frontZ + backZ) * 0.5f);
            apron.transform.localScale = new Vector3(toX - fromX, 0.06f, backZ - frontZ);

            // 站在樹籬後面看比賽的人：終點附近最擠，越往起點越稀疏
            for (int row = 0; row < 3; row++)
            {
                float z = frontZ + 0.8f + row * 1.3f;
                for (float x = fromX; x < toX; x += 0.85f)
                {
                    float nearFinish = Mathf.Clamp01(1f - Mathf.Abs(x - TrackLayout.FinishX) / 140f);
                    float chance = density * (0.35f + 0.65f * nearFinish) * (1f - row * 0.2f);
                    if (random.NextDouble() < chance)
                    {
                        float jitter = ((float)random.NextDouble() - 0.5f) * 0.4f;
                        crowd.AddStanding(new Vector3(x + jitter, 0.06f, z + jitter));
                    }
                }
            }
        }

        /// <summary>建一座階梯看台（含觀眾與屋頂），回傳看台後緣的 Z。</summary>
        private static float BuildStand(Transform parent, StandSpec spec, float frontZ, CrowdBuilder crowd,
                                        System.Random random, float density, bool withFlags)
        {
            float length = spec.ToX - spec.FromX;
            float centerX = (spec.FromX + spec.ToX) * 0.5f;
            float backZ = frontZ + spec.Tiers * TierDepth;

            for (int tier = 0; tier < spec.Tiers; tier++)
            {
                float top = StandBaseHeight + tier * TierRise;
                float z = frontZ + tier * TierDepth;

                // 每一階是一塊實心方塊（正面是水泥色），上面鋪一層座椅色
                GameObject step = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Tier", parent, ConcreteColor);
                step.transform.localPosition = new Vector3(centerX, top * 0.5f, z + TierDepth * 0.5f);
                step.transform.localScale = new Vector3(length, top, TierDepth);

                GameObject seats = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Seats", parent, SeatColor);
                seats.transform.localPosition = new Vector3(centerX, top + 0.02f, z + TierDepth * 0.55f);
                seats.transform.localScale = new Vector3(length, 0.04f, TierDepth * 0.7f);

                for (float x = spec.FromX + 0.6f; x < spec.ToX - 0.6f; x += SeatSpacing)
                {
                    if (random.NextDouble() < density)
                    {
                        crowd.AddSeated(new Vector3(x, top + 0.04f, z + TierDepth * 0.6f));
                    }
                }
            }

            float roofY = StandBaseHeight + spec.Tiers * TierRise + RoofClearance;
            BuildRoof(parent, spec, frontZ, backZ, roofY, withFlags);
            return backZ;
        }

        private static void BuildRoof(Transform parent, StandSpec spec, float frontZ, float backZ, float roofY,
                                      bool withFlags)
        {
            float length = spec.ToX - spec.FromX;
            float centerX = (spec.FromX + spec.ToX) * 0.5f;

            // 後牆與後排柱子撐起懸臂屋頂；前緣不放柱子，才不會擋住觀眾
            GameObject wall = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "BackWall", parent, WallColor);
            wall.transform.localPosition = new Vector3(centerX, roofY * 0.5f, backZ + 0.25f);
            wall.transform.localScale = new Vector3(length, roofY, 0.5f);
            BuildBoxWindows(parent, spec, backZ, StandBaseHeight + spec.Tiers * TierRise + 0.9f, roofY - 0.5f);

            // 屋頂底面用淺色：鏡頭是仰角，深色屋頂會變成畫面上方一整片黑
            float roofFrontZ = frontZ - 1.2f;
            GameObject roof = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Roof", parent, RoofColor);
            roof.transform.localPosition = new Vector3(centerX, roofY, (roofFrontZ + backZ) * 0.5f + 0.25f);
            roof.transform.localScale = new Vector3(length + 1f, 0.3f, backZ - roofFrontZ + 0.5f);

            GameObject fascia = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Fascia", parent, FasciaColor);
            fascia.transform.localPosition = new Vector3(centerX, roofY + 0.35f, roofFrontZ);
            fascia.transform.localScale = new Vector3(length + 1.2f, 1.0f, 0.3f);

            GameObject trim = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "FasciaTrim", parent, TrimColor);
            trim.transform.localPosition = new Vector3(centerX, roofY - 0.2f, roofFrontZ - 0.05f);
            trim.transform.localScale = new Vector3(length + 1.2f, 0.14f, 0.3f);

            if (withFlags)
            {
                BuildFlags(parent, spec, roofFrontZ + 1.5f, roofY + 0.85f);
            }
        }

        /// <summary>看台最上排後方的包廂玻璃帶，每 4 單位一根白色窗框，後牆才不是一整片空白。</summary>
        private static void BuildBoxWindows(Transform parent, StandSpec spec, float wallZ, float bottomY, float topY)
        {
            if (topY - bottomY < 0.8f)
            {
                return;
            }

            float length = spec.ToX - spec.FromX;
            GameObject glass = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "BoxWindows", parent, WindowColor);
            glass.transform.localPosition = new Vector3((spec.FromX + spec.ToX) * 0.5f, (bottomY + topY) * 0.5f, wallZ - 0.03f);
            glass.transform.localScale = new Vector3(length - 1.5f, topY - bottomY, 0.06f);

            for (float x = spec.FromX + 1f; x < spec.ToX - 0.5f; x += 4f)
            {
                GameObject mullion = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Mullion", parent, TrimColor);
                mullion.transform.localPosition = new Vector3(x, (bottomY + topY) * 0.5f, wallZ - 0.08f);
                mullion.transform.localScale = new Vector3(0.18f, topY - bottomY, 0.06f);
            }
        }

        private static void BuildFlags(Transform parent, StandSpec spec, float z, float baseY)
        {
            int index = 0;
            for (float x = spec.FromX + 4f; x < spec.ToX - 2f; x += 10f)
            {
                GameObject pole = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "FlagPole", parent, TrimColor);
                pole.transform.localPosition = new Vector3(x, baseY + 1.6f, z);
                pole.transform.localScale = new Vector3(0.1f, 3.2f, 0.1f);

                Color color = FlagColors[index++ % FlagColors.Length];
                GameObject flag = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Flag", parent, color);
                flag.transform.localPosition = new Vector3(x + 0.7f, baseY + 2.8f, z);
                flag.transform.localScale = new Vector3(1.3f, 0.8f, 0.04f);
            }
        }

        /// <summary>終點後方的會員會館：兩層樓、一排排窗、綠色屋簷。</summary>
        private static void BuildClubhouse(Transform parent, float fromX, float frontZ)
        {
            const float width = 26f;
            const float depth = 14f;
            const float height = 10f;
            float centerX = fromX + width * 0.5f;

            GameObject body = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Clubhouse", parent, WallColor);
            body.transform.localPosition = new Vector3(centerX, height * 0.5f, frontZ + depth * 0.5f);
            body.transform.localScale = new Vector3(width, height, depth);

            GameObject eave = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "ClubhouseRoof", parent, FasciaColor);
            eave.transform.localPosition = new Vector3(centerX, height + 0.4f, frontZ + depth * 0.5f);
            eave.transform.localScale = new Vector3(width + 1.6f, 0.8f, depth + 1.6f);

            for (int floor = 0; floor < 2; floor++)
            {
                for (float x = fromX + 2f; x < fromX + width - 1.5f; x += 3f)
                {
                    GameObject window = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "Window", parent, WindowColor);
                    window.transform.localPosition = new Vector3(x + 0.8f, 3f + floor * 4f, frontZ - 0.02f);
                    window.transform.localScale = new Vector3(1.6f, 2f, 0.1f);
                }
            }
        }

        /// <summary>勝利柱：終點線延長線上、遠側欄杆後方的紅白圓牌，賽馬場終點的經典標誌。</summary>
        private static void BuildWinningPost(Transform parent, float railZ)
        {
            float x = TrackLayout.FinishX;
            float z = railZ + 0.5f;

            GameObject pole = TrackBuilder.CreatePrimitive(PrimitiveType.Cube, "WinningPost", parent, TrimColor);
            pole.transform.localPosition = new Vector3(x, 2f, z);
            pole.transform.localScale = new Vector3(0.18f, 4f, 0.18f);

            // Cylinder 的軸是 Y，轉 90 度讓圓面朝向鏡頭（−Z）
            GameObject disc = TrackBuilder.CreatePrimitive(PrimitiveType.Cylinder, "WinningDisc", parent, PostRed);
            disc.transform.localPosition = new Vector3(x, 4.4f, z - 0.1f);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(1.5f, 0.04f, 1.5f);

            GameObject inner = TrackBuilder.CreatePrimitive(PrimitiveType.Cylinder, "WinningDiscInner", parent, TrimColor);
            inner.transform.localPosition = new Vector3(x, 4.4f, z - 0.16f);
            inner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            inner.transform.localScale = new Vector3(0.8f, 0.04f, 0.8f);
        }

        // ---- 遠景 ----

        private static void BuildTreeLine(Transform parent, float nearZ, System.Random random)
        {
            float fromX = TrackLayout.StartX - SceneryBeforeStart;
            float toX = TrackLayout.FinishX + SceneryAfterFinish;
            for (int row = 0; row < 3; row++)
            {
                float z = nearZ + row * 9f;
                for (float x = fromX + (float)random.NextDouble() * TreeSpacing; x < toX; x += TreeSpacing)
                {
                    string model = TreeModels[random.Next(TreeModels.Length)];
                    float scale = 7f + (float)random.NextDouble() * 3f + row * 1.5f;
                    float jitterZ = ((float)random.NextDouble() - 0.5f) * 4f;
                    Color leaves = LeafColors[random.Next(LeafColors.Length)];
                    Place(parent, NatureRoot + model, scale, (float)random.NextDouble() * 360f,
                        new Vector3(x + ((float)random.NextDouble() - 0.5f) * 3f, 0f, z + jitterZ), leaves);
                }
            }
        }

        /// <summary>遠方的綠色丘陵：壓扁的球體，只為了讓天際線不是一條直線。</summary>
        private static void BuildHills(Transform parent, System.Random random)
        {
            for (int i = 0; i < 9; i++)
            {
                float x = TrackLayout.StartX - 220f + i * 75f + (float)random.NextDouble() * 30f;
                float z = 190f + (float)random.NextDouble() * 80f;
                float width = 90f + (float)random.NextDouble() * 80f;
                float height = 22f + (float)random.NextDouble() * 26f;

                Color color = Color.Lerp(HillColor, new Color(0.55f, 0.66f, 0.72f), (z - 190f) / 160f + 0.2f);
                GameObject hill = TrackBuilder.CreatePrimitive(PrimitiveType.Sphere, "Hill", parent, color);
                hill.transform.localPosition = new Vector3(x, 0f, z);
                hill.transform.localScale = new Vector3(width, height, width * 0.6f);
            }
        }

        // ---- 共用 ----

        /// <param name="leafColor">不為 null 時把樹葉換成這個顏色、樹幹換成咖啡色（只用在樹上）。</param>
        private static void Place(Transform parent, string resourcePath, float scale, float yaw, Vector3 position,
                                  Color? leafColor)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                if (ReportedMissing.Add(resourcePath))
                {
                    Debug.LogWarning("[RacecourseScenery] 找不到模型 Resources/" + resourcePath + "，略過。");
                }

                return;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
            instance.transform.localScale = prefab.transform.localScale * scale;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (leafColor.HasValue)
                {
                    RecolorTree(renderer, leafColor.Value);
                }
            }
        }

        /// <summary>依模型材質名稱（leafs…／wood…）換色，換上的材質由 MaterialLibrary 快取共用。</summary>
        private static void RecolorTree(Renderer renderer, Color leafColor)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                string name = materials[i] != null ? materials[i].name.ToLowerInvariant() : "";
                if (name.Contains("leaf"))
                {
                    materials[i] = MaterialLibrary.Opaque(leafColor);
                }
                else if (name.Contains("wood") || name.Contains("bark") || name.Contains("trunk"))
                {
                    materials[i] = MaterialLibrary.Opaque(BarkColor);
                }
            }

            renderer.sharedMaterials = materials;
        }
    }
}
