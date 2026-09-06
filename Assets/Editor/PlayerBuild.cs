using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// Windows 版的建置流程。同時提供選單與命令列兩種入口，
    /// 命令列版本讓 CI 或本機腳本可以在不開編輯器的情況下出版本。
    /// </summary>
    public static class PlayerBuild
    {
        private const string OutputPath = "Build/Windows/HorseRace.exe";
        private const string MainScene = "Assets/Scenes/SampleScene.unity";

        [MenuItem("HorseRace/建置 Windows 版")]
        public static void BuildFromMenu()
        {
            BuildReport report = Build();
            Debug.Log("[PlayerBuild] 建置結果：" + report.summary.result
                      + "，輸出於 " + OutputPath);
        }

        /// <summary>
        /// 命令列入口：
        /// Unity.exe -batchmode -quit -projectPath . -executeMethod HorseRace.EditorTools.PlayerBuild.BuildFromCommandLine
        /// </summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                BuildReport report = Build();
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("[PlayerBuild] 建置失敗，共 "
                                   + report.summary.totalErrors + " 個錯誤。");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log("[PlayerBuild] 建置成功：" + OutputPath);
            }
            catch (Exception error)
            {
                Debug.LogError("[PlayerBuild] 建置過程發生例外："
                               + error.GetType().Name + " - " + error.Message);
                EditorApplication.Exit(1);
            }
        }

        private static BuildReport Build()
        {
            // 材質是執行期用 Shader.Find 建的，少了這一步打包出來會整片變洋紅色
            AlwaysIncludedShaders.EnsureIncluded();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            return BuildPipeline.BuildPlayer(options);
        }
    }
}
