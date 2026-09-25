using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 大螢幕的演出設定（開場動畫等）。不影響任何遊戲規則，放在設定檔是為了換素材不必改程式。
    /// </summary>
    [Serializable]
    public sealed class PresentationConfig
    {
        /// <summary>程式啟動時是否先播開場。伺服器與外網通道會在開場播放期間於背景準備好。</summary>
        public bool PlayIntro = true;

        /// <summary>
        /// 開場影片，相對於 StreamingAssets 的路徑。建議 H.264 編碼的 mp4。
        /// 檔案不存在時改播程式產生的預設開場畫面。
        /// </summary>
        public string IntroVideo = DefaultIntroVideo;

        /// <summary>沒有影片時，預設開場畫面的長度（秒）。</summary>
        public double PlaceholderSeconds = 6.0;

        /// <summary>主持人能否按空白鍵、Enter 或點滑鼠略過開場。</summary>
        public bool IntroSkippable = true;

        public const string DefaultIntroVideo = "intro/intro.mp4";

        public void Validate()
        {
            if (IntroVideo == null)
            {
                IntroVideo = "";
            }

            PlaceholderSeconds = ConfigMath.Clamp(PlaceholderSeconds, 1.0, 60.0);
        }
    }
}
