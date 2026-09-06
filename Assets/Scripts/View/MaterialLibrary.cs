using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 執行期產生並快取材質。所有視覺都用程式建立，不依賴任何匯入的素材，
    /// 使用者提供正式素材前也能直接跑起來。
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        private static Shader _shader;

        /// <summary>取得（或建立）指定顏色的不透明材質。相同顏色共用同一份。</summary>
        public static Material Opaque(Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            int key = ComputeKey(color, smoothness, metallic);

            Material cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Material material = new Material(ResolveShader());
            material.name = "Auto_" + ColorUtility.ToHtmlStringRGB(color);
            material.color = color;

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            Cache[key] = material;
            return material;
        }

        /// <summary>把 "#RRGGBB" 解析成 Color，失敗時回傳指定的替代色而不是讓畫面變黑。</summary>
        public static Color ParseHex(string hex, Color fallback)
        {
            Color parsed;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out parsed))
            {
                return parsed;
            }

            Debug.LogWarning("[MaterialLibrary] 無法解析色碼 \"" + hex + "\"，改用替代色。");
            return fallback;
        }

        /// <summary>依比例把顏色調暗，用於做出鬃毛、陰影等層次。</summary>
        public static Color Darken(Color color, float amount)
        {
            return Color.Lerp(color, Color.black, Mathf.Clamp01(amount));
        }

        /// <summary>依比例把顏色調亮，用於騎師服等需要與馬身區隔的部位。</summary>
        public static Color Lighten(Color color, float amount)
        {
            return Color.Lerp(color, Color.white, Mathf.Clamp01(amount));
        }

        private static Shader ResolveShader()
        {
            if (_shader != null)
            {
                return _shader;
            }

            _shader = Shader.Find("Standard");
            if (_shader == null)
            {
                // 建置時若 Standard 被剝離，所有材質會變成洋紅色。
                // Assets/Editor/AlwaysIncludedShaders.cs 會在建置前自動補上，
                // 這裡只是最後一道保險。
                Debug.LogError("[MaterialLibrary] 找不到 Standard 著色器，改用 Legacy Shaders/Diffuse。"
                               + "請確認 Project Settings > Graphics > Always Included Shaders 含有 Standard。");
                _shader = Shader.Find("Legacy Shaders/Diffuse");
            }

            return _shader;
        }

        private static int ComputeKey(Color color, float smoothness, float metallic)
        {
            unchecked
            {
                int key = Mathf.RoundToInt(color.r * 255f);
                key = (key * 397) ^ Mathf.RoundToInt(color.g * 255f);
                key = (key * 397) ^ Mathf.RoundToInt(color.b * 255f);
                key = (key * 397) ^ Mathf.RoundToInt(color.a * 255f);
                key = (key * 397) ^ Mathf.RoundToInt(smoothness * 100f);
                key = (key * 397) ^ Mathf.RoundToInt(metallic * 100f);
                return key;
            }
        }
    }
}
