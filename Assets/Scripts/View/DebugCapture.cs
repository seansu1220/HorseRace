using System;
using System.IO;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 以命令列參數 <c>-capture &lt;資料夾&gt;</c> 啟用，每隔固定秒數把畫面存成 PNG。
    ///
    /// 用 <see cref="ScreenCapture"/> 而不是外部截圖工具，是因為它只會擷取遊戲本身的
    /// 畫格緩衝區——不會拍到桌面上的其他視窗與個人內容。
    /// 不帶參數執行時完全不會被掛上，對正式執行沒有任何影響。
    /// </summary>
    public sealed class DebugCapture : MonoBehaviour
    {
        private const string Flag = "-capture";
        private const float IntervalSeconds = 4f;
        private const int MaxShots = 24;

        private string _outputDirectory;
        private float _nextCaptureTime;
        private int _index;

        /// <summary>命令列有指定輸出資料夾時，才把擷取元件掛到指定物件上。</summary>
        public static void AttachIfRequested(GameObject host)
        {
            string directory = ReadDirectoryArgument();
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            DebugCapture capture = host.AddComponent<DebugCapture>();
            capture.Initialize(directory);
        }

        private void Initialize(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _outputDirectory = directory;
                _nextCaptureTime = Time.time + IntervalSeconds;
                Debug.Log("[DebugCapture] 已啟用畫面擷取，輸出至 " + directory);
            }
            catch (Exception error)
            {
                Debug.LogError("[DebugCapture] 無法建立輸出資料夾 " + directory + "，停用擷取。原因："
                               + error.GetType().Name + " - " + error.Message);
                enabled = false;
            }
        }

        private void Update()
        {
            if (_outputDirectory == null || _index >= MaxShots || Time.time < _nextCaptureTime)
            {
                return;
            }

            _nextCaptureTime = Time.time + IntervalSeconds;
            _index++;

            string path = Path.Combine(_outputDirectory, "frame-" + _index.ToString("D2") + ".png");

            try
            {
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[DebugCapture] 已請求擷取 " + path);
            }
            catch (Exception error)
            {
                Debug.LogError("[DebugCapture] 擷取失敗：" + error.GetType().Name + " - " + error.Message);
                enabled = false;
            }
        }

        private static string ReadDirectoryArgument()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                for (int i = 0; i < arguments.Length - 1; i++)
                {
                    if (string.Equals(arguments[i], Flag, StringComparison.OrdinalIgnoreCase))
                    {
                        return arguments[i + 1];
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[DebugCapture] 讀取命令列參數失敗：" + error.Message);
            }

            return null;
        }
    }
}
