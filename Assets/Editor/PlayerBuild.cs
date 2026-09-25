using System;
using System.IO;
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

        /// <summary>
        /// 大螢幕會自己啟動中繼伺服器，所以建置版旁邊要有一份 server/。
        /// node_modules 一起帶走，現場電腦就不必連網 npm install。
        /// </summary>
        private const string RelaySourceDirectory = "server";
        private static readonly string[] RelayEntries =
        {
            "src", "public", "node_modules", "package.json", "package-lock.json"
        };

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

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                CopyRelayServer();
            }

            return report;
        }

        /// <summary>
        /// 把中繼伺服器複製到 exe 旁邊。失敗只記錯誤、不讓整個建置失敗：
        /// 大螢幕照樣能跑，只是要改成手動開伺服器或在設定檔指定 ServerDirectory。
        /// </summary>
        private static void CopyRelayServer()
        {
            string target = Path.Combine(Path.GetDirectoryName(OutputPath) ?? ".", RelaySourceDirectory);

            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }

                Directory.CreateDirectory(target);

                foreach (string entry in RelayEntries)
                {
                    string source = Path.Combine(RelaySourceDirectory, entry);
                    string destination = Path.Combine(target, entry);

                    if (Directory.Exists(source))
                    {
                        CopyDirectory(source, destination);
                    }
                    else if (File.Exists(source))
                    {
                        File.Copy(source, destination, true);
                    }
                    else
                    {
                        Debug.LogWarning("[PlayerBuild] 找不到 " + source + "，建置版的中繼伺服器可能不完整"
                                         + "（node_modules 缺少時，第一次執行會自動 npm install）。");
                    }
                }

                Debug.Log("[PlayerBuild] 已複製中繼伺服器到 " + target);
            }
            catch (Exception error)
            {
                Debug.LogError("[PlayerBuild] 複製中繼伺服器失敗：" + error.GetType().Name + " - " + error.Message
                               + "。建置版需要手動把 server 資料夾放到 exe 旁邊。");
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
    }
}
