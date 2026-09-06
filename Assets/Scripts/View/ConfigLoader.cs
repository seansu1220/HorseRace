using System;
using System.IO;
using System.Text;
using HorseRace.Core;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 從 StreamingAssets 讀取設定。放在 StreamingAssets 而不是 ScriptableObject 的理由：
    /// 建置後仍是一般檔案，現場要調參數直接用記事本改再重開程式就好，不必重新出版本。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>相對於 StreamingAssets 的設定檔路徑。</summary>
        public const string RelativePath = "config/race.json";

        public static string FullPath
        {
            get { return Path.Combine(Application.streamingAssetsPath, RelativePath); }
        }

        /// <summary>
        /// 讀取設定。任何失敗都退回內建預設值而不是拋例外——
        /// 現場活動中不能因為一個手殘的 JSON 就讓程式開不起來。
        /// </summary>
        public static GameConfig Load()
        {
            GameConfig config = ReadFromDisk();

            if (config == null)
            {
                config = GameConfig.CreateDefault();
            }

            config.Validate();
            return config;
        }

        private static GameConfig ReadFromDisk()
        {
            string path = FullPath;

            try
            {
                if (!File.Exists(path))
                {
                    Debug.Log("[ConfigLoader] 找不到 " + path + "，使用內建預設設定。");
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning("[ConfigLoader] " + path + " 是空檔案，使用內建預設設定。");
                    return null;
                }

                GameConfig parsed = JsonUtility.FromJson<GameConfig>(json);
                if (parsed == null)
                {
                    Debug.LogWarning("[ConfigLoader] " + path + " 解析結果為空，使用內建預設設定。");
                    return null;
                }

                Debug.Log("[ConfigLoader] 已載入設定：" + path);
                return parsed;
            }
            catch (Exception error)
            {
                Debug.LogError("[ConfigLoader] 讀取 " + path + " 失敗，改用內建預設設定。原因："
                               + error.GetType().Name + " - " + error.Message);
                return null;
            }
        }
    }
}
