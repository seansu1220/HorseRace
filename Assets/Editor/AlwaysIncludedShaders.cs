using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 確保執行期用到的著色器不會在建置時被剝離。
    ///
    /// 本專案所有材質都是執行期用 <c>Shader.Find("Standard")</c> 建立的，
    /// 沒有任何場景資產引用到它。Unity 建置時會判定「沒人用」而把它拿掉，
    /// 結果就是打包出來的程式所有東西都變成洋紅色。
    /// 這支腳本在每次建置前自動把需要的著色器補進 Always Included Shaders，
    /// 使用者不需要記得去 Project Settings 手動設定。
    /// </summary>
    public sealed class AlwaysIncludedShaders : IPreprocessBuildWithReport
    {
        private static readonly string[] RequiredShaders =
        {
            "Standard",
            "UI/Default",
            "Legacy Shaders/Diffuse"
        };

        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureIncluded();
        }

        [MenuItem("HorseRace/確保著色器已加入建置")]
        public static void EnsureIncludedFromMenu()
        {
            int added = EnsureIncluded();
            Debug.Log(added == 0
                ? "[AlwaysIncludedShaders] 所有必要著色器都已在清單中。"
                : "[AlwaysIncludedShaders] 已補上 " + added + " 支著色器。");
        }

        /// <summary>回傳這次實際補上的著色器數量。</summary>
        public static int EnsureIncluded()
        {
            Object[] settingsAssets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");

            if (settingsAssets == null || settingsAssets.Length == 0)
            {
                Debug.LogWarning("[AlwaysIncludedShaders] 讀不到 GraphicsSettings.asset，跳過設定。");
                return 0;
            }

            SerializedObject settings = new SerializedObject(settingsAssets[0]);
            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("[AlwaysIncludedShaders] 找不到 m_AlwaysIncludedShaders 欄位，跳過設定。");
                return 0;
            }

            HashSet<Shader> existing = new HashSet<Shader>();
            for (int i = 0; i < list.arraySize; i++)
            {
                Shader entry = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (entry != null)
                {
                    existing.Add(entry);
                }
            }

            int added = 0;
            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                Shader shader = Shader.Find(RequiredShaders[i]);
                if (shader == null)
                {
                    Debug.LogWarning("[AlwaysIncludedShaders] 找不到著色器 " + RequiredShaders[i] + "。");
                    continue;
                }

                if (existing.Contains(shader))
                {
                    continue;
                }

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                existing.Add(shader);
                added++;
            }

            if (added > 0)
            {
                settings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            return added;
        }
    }
}
