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

        /// <summary>主持人能否按空白鍵或 Enter 略過開場（滑鼠點擊不算，避免點視窗時誤觸）。</summary>
        public bool IntroSkippable = true;

        /// <summary>
        /// 開場內容播完時 QRCode 還沒準備好（外網通道約需 10～20 秒），最多再等幾秒才揭開等待入場畫面。
        /// 等待期間預設畫面繼續動、影片停在最後一格並顯示「連線準備中」。設 0 則播完就結束。
        /// </summary>
        public double IntroMaxWaitSeconds = 30.0;

        /// <summary>待機與下注時從馬的正面拍起跑閘；false 則用側面的起跑閘全景。</summary>
        public bool StartCameraFromFront = true;

        /// <summary>開跑後鏡頭在正面停留幾秒（讓觀眾看到馬衝出閘門），才開始轉到側面跟拍。</summary>
        public double StartCameraHoldSeconds = 1.0;

        /// <summary>從正面轉到側面跟拍花幾秒。太短像瞬移，太長馬會先跑出畫面。</summary>
        public double StartCameraBlendSeconds = 2.5;

        public const string DefaultIntroVideo = "intro/intro.mp4";

        public void Validate()
        {
            if (IntroVideo == null)
            {
                IntroVideo = "";
            }

            PlaceholderSeconds = ConfigMath.Clamp(PlaceholderSeconds, 1.0, 60.0);
            IntroMaxWaitSeconds = ConfigMath.Clamp(IntroMaxWaitSeconds, 0.0, 180.0);
            StartCameraHoldSeconds = ConfigMath.Clamp(StartCameraHoldSeconds, 0.0, 10.0);
            StartCameraBlendSeconds = ConfigMath.Clamp(StartCameraBlendSeconds, 0.3, 10.0);
        }
    }
}
