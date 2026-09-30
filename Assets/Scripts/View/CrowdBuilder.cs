using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 觀眾群：每個人由幾個方塊（褲子、上衣、頭）組成。上千人各自一個 GameObject 太重，
    /// 所以先收集所有方塊的位置與顏色，最後依顏色合併成少數幾個網格，一種顏色只有一次繪製。
    /// </summary>
    public sealed class CrowdBuilder
    {
        private static readonly Color[] ShirtColors =
        {
            new Color(0.86f, 0.22f, 0.22f), new Color(0.20f, 0.45f, 0.82f), new Color(0.95f, 0.78f, 0.22f),
            new Color(0.25f, 0.66f, 0.38f), new Color(0.94f, 0.94f, 0.92f), new Color(0.58f, 0.32f, 0.68f),
            new Color(0.95f, 0.55f, 0.20f), new Color(0.35f, 0.75f, 0.85f)
        };

        private static readonly Color[] SkinColors =
        {
            new Color(0.96f, 0.80f, 0.66f), new Color(0.84f, 0.64f, 0.48f), new Color(0.58f, 0.40f, 0.28f)
        };

        private static readonly Color PantsColor = new Color(0.20f, 0.22f, 0.28f);

        /// <summary>每個合併網格最多幾個方塊（方塊 24 個頂點，留在 32 位元索引也不會過大的範圍）。</summary>
        private const int MaxBoxesPerMesh = 6000;

        private readonly Dictionary<Color, List<Matrix4x4>> _boxesByColor = new Dictionary<Color, List<Matrix4x4>>();
        private readonly System.Random _random;

        public CrowdBuilder(int seed)
        {
            _random = new System.Random(seed);
        }

        /// <summary>站著的觀眾，腳底在 <paramref name="feet"/>，面向 −Z（賽道）。</summary>
        public void AddStanding(Vector3 feet)
        {
            float height = 0.9f + (float)_random.NextDouble() * 0.2f;
            AddBox(PantsColor, feet + new Vector3(0f, 0.4f * height, 0f), new Vector3(0.38f, 0.8f * height, 0.24f));
            AddBox(RandomShirt(), feet + new Vector3(0f, 1.1f * height, 0f), new Vector3(0.48f, 0.62f * height, 0.3f));
            AddBox(RandomSkin(), feet + new Vector3(0f, 1.58f * height, 0f), new Vector3(0.26f, 0.28f, 0.26f));
        }

        /// <summary>坐著的觀眾（看台上），座面在 <paramref name="seat"/>。只做上半身，腳被前一排擋住看不到。</summary>
        public void AddSeated(Vector3 seat)
        {
            AddBox(RandomShirt(), seat + new Vector3(0f, 0.32f, 0f), new Vector3(0.46f, 0.62f, 0.32f));
            AddBox(RandomSkin(), seat + new Vector3(0f, 0.8f, 0f), new Vector3(0.26f, 0.28f, 0.26f));
        }

        /// <summary>把收集到的方塊依顏色合併成網格，放在 <paramref name="parent"/> 底下。</summary>
        public void Build(Transform parent, string name)
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cube == null)
            {
                Debug.LogWarning("[CrowdBuilder] 取不到內建方塊網格，略過觀眾。");
                return;
            }

            foreach (KeyValuePair<Color, List<Matrix4x4>> group in _boxesByColor)
            {
                for (int start = 0; start < group.Value.Count; start += MaxBoxesPerMesh)
                {
                    int count = Mathf.Min(MaxBoxesPerMesh, group.Value.Count - start);
                    BuildChunk(parent, name, cube, group.Key, group.Value, start, count);
                }
            }

            _boxesByColor.Clear();
        }

        private static void BuildChunk(Transform parent, string name, Mesh cube, Color color,
                                       List<Matrix4x4> boxes, int start, int count)
        {
            CombineInstance[] parts = new CombineInstance[count];
            for (int i = 0; i < count; i++)
            {
                parts[i] = new CombineInstance { mesh = cube, transform = boxes[start + i] };
            }

            Mesh combined = new Mesh();
            combined.name = name;
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(parts, true, true);

            GameObject chunk = new GameObject(name);
            chunk.transform.SetParent(parent, false);
            chunk.AddComponent<MeshFilter>().sharedMesh = combined;
            MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Opaque(color, 0.1f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void AddBox(Color color, Vector3 center, Vector3 size)
        {
            List<Matrix4x4> boxes;
            if (!_boxesByColor.TryGetValue(color, out boxes))
            {
                boxes = new List<Matrix4x4>();
                _boxesByColor[color] = boxes;
            }

            boxes.Add(Matrix4x4.TRS(center, Quaternion.identity, size));
        }

        private Color RandomShirt()
        {
            return ShirtColors[_random.Next(ShirtColors.Length)];
        }

        private Color RandomSkin()
        {
            return SkinColors[_random.Next(SkinColors.Length)];
        }
    }
}
