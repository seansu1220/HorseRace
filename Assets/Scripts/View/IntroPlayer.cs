using System;
using System.Collections.Generic;
using System.IO;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace HorseRace.View
{
    /// <summary>
    /// 程式啟動時的開場。有影片就播影片，沒有（或播放失敗）就播程式產生的預設畫面，
    /// 播完或被略過後淡出、通知 <see cref="Finished"/>，然後自我銷毀。
    ///
    /// 開場只是蓋在最上層的一張畫布：底下的伺服器、外網通道、等待入場畫面照常在準備，
    /// 開場結束時 QRCode 通常已經就緒。
    /// 換開場影片只要替換 StreamingAssets 裡的檔案（路徑見 <see cref="PresentationConfig.IntroVideo"/>）。
    /// </summary>
    public sealed class IntroPlayer : MonoBehaviour
    {
        /// <summary>蓋在大螢幕介面（排序 0）之上。</summary>
        private const int CanvasSortOrder = 100;

        private const float FadeOutSeconds = 0.5f;

        /// <summary>剛啟動的瞬間不接受略過，避免上一個畫面殘留的按鍵直接把開場跳掉。</summary>
        private const float SkipGuardSeconds = 0.4f;

        /// <summary>影片準備（解碼器初始化）超過這個時間就放棄，改播預設畫面。</summary>
        private const float PrepareTimeoutSeconds = 8f;

        private static readonly Color BackdropColor = new Color(0.04f, 0.075f, 0.06f);
        private static readonly Color TitleColor = new Color(0.89f, 0.71f, 0.29f);

        private PresentationConfig _config;
        private Color[] _horseColors;
        private CanvasGroup _group;
        private RectTransform _root;

        private VideoPlayer _video;
        private RenderTexture _videoTexture;
        private RawImage _videoImage;
        private AspectRatioFitter _videoFitter;
        private float _prepareDeadline;

        private RectTransform _placeholder;
        private Text _title;
        private RectTransform[] _runners;
        private float _placeholderStartedAt;
        private float _placeholderEndsAt = -1f;

        private float _startedAt;
        private bool _finishing;
        private float _fadeStartedAt;

        /// <summary>QRCode 與連線是否已準備好，可以揭開等待入場畫面。</summary>
        private Func<bool> _isReady;

        /// <summary>開場內容（影片或預設畫面的基本長度）播完的時間；還沒播完為負值。</summary>
        private float _contentEndedAt = -1f;
        private Text _waitLabel;

        /// <summary>開場結束（已淡出）時觸發，之後物件會自我銷毀。</summary>
        public event Action Finished;

        /// <summary>建立並開始播放開場。</summary>
        /// <param name="isReady">QRCode 與連線是否已就緒；開場播完時若還沒好，會繼續等一段時間。</param>
        public static IntroPlayer Create(Transform parent, GameConfig config, Func<bool> isReady)
        {
            GameObject root = new GameObject("Intro");
            root.transform.SetParent(parent, false);

            IntroPlayer intro = root.AddComponent<IntroPlayer>();
            intro._isReady = isReady;
            intro.Begin(config.Presentation, ReadHorseColors(config.Roster));
            return intro;
        }

        private void Begin(PresentationConfig config, Color[] horseColors)
        {
            _config = config;
            _horseColors = horseColors;
            _startedAt = Time.unscaledTime;

            BuildCanvas();

            string videoPath = ResolveVideoPath(config.IntroVideo);
            if (videoPath != null)
            {
                StartVideo(videoPath);
            }
            else
            {
                StartPlaceholder();
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (_finishing)
            {
                UpdateFadeOut(now);
                return;
            }

            if (_config.IntroSkippable && now - _startedAt > SkipGuardSeconds && SkipRequested())
            {
                Debug.Log("[IntroPlayer] 開場被略過。");
                Finish();
                return;
            }

            if (_video != null && !_video.isPrepared && now > _prepareDeadline)
            {
                Debug.LogWarning("[IntroPlayer] 開場影片 " + PrepareTimeoutSeconds + " 秒內無法開始播放，改播預設開場。");
                FallBackToPlaceholder();
            }

            if (_placeholder != null)
            {
                AnimatePlaceholder(now);
                if (now >= _placeholderEndsAt && _contentEndedAt < 0f)
                {
                    _contentEndedAt = now;
                }
            }

            if (_contentEndedAt >= 0f)
            {
                WaitUntilReadyThenFinish(now);
            }
        }

        /// <summary>
        /// 開場內容播完之後：連線已就緒就結束；還沒好就顯示「連線準備中」繼續等，
        /// 超過 <see cref="PresentationConfig.IntroMaxWaitSeconds"/> 就不等了（等待畫面自己也會顯示進度）。
        /// </summary>
        private void WaitUntilReadyThenFinish(float now)
        {
            bool ready = _isReady == null || SafeIsReady();
            bool waitedTooLong = now - _contentEndedAt >= (float)_config.IntroMaxWaitSeconds;

            if (ready || waitedTooLong)
            {
                Finish();
                return;
            }

            ShowWaitLabel();
        }

        private bool SafeIsReady()
        {
            try
            {
                return _isReady();
            }
            catch (Exception error)
            {
                Debug.LogWarning("[IntroPlayer] 檢查連線狀態失敗，直接結束開場：" + error.Message);
                return true;
            }
        }

        private void ShowWaitLabel()
        {
            if (_waitLabel != null)
            {
                return;
            }

            _waitLabel = UiFactory.Label(_root, "Waiting", "連線準備中…", 34,
                TextAnchor.MiddleCenter, UiFactory.TextColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_waitLabel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(800f, 56f));

            Outline outline = _waitLabel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private void OnDestroy()
        {
            ReleaseVideo();
        }

        private static bool SkipRequested()
        {
            return Input.GetKeyDown(KeyCode.Space)
                   || Input.GetKeyDown(KeyCode.Return)
                   || Input.GetKeyDown(KeyCode.KeypadEnter)
                   || Input.GetMouseButtonDown(0);
        }

        // ---- 結束 ----

        private void Finish()
        {
            if (_finishing)
            {
                return;
            }

            _finishing = true;
            _fadeStartedAt = Time.unscaledTime;

            // 先停聲音；畫面停在最後一格一起淡出
            if (_video != null)
            {
                _video.Pause();
            }
        }

        private void UpdateFadeOut(float now)
        {
            float progress = (now - _fadeStartedAt) / FadeOutSeconds;
            _group.alpha = Mathf.Clamp01(1f - progress);

            if (progress < 1f)
            {
                return;
            }

            Action handler = Finished;
            Finished = null;
            if (handler != null)
            {
                handler();
            }

            Destroy(gameObject);
        }

        // ---- 影片 ----

        /// <summary>回傳影片的完整路徑；沒設定或檔案不存在回傳 null（改播預設畫面）。</summary>
        private static string ResolveVideoPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            try
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }

                Debug.Log("[IntroPlayer] 找不到開場影片 " + fullPath + "，播放預設開場。");
            }
            catch (Exception error)
            {
                Debug.LogWarning("[IntroPlayer] 開場影片路徑無效（" + relativePath + "）："
                                 + error.GetType().Name + " - " + error.Message);
            }

            return null;
        }

        private void StartVideo(string fullPath)
        {
            RectTransform imageRect = UiFactory.Node(_root, "Video");
            UiFactory.Stretch(imageRect, 0f, 0f, 0f, 0f);
            _videoImage = imageRect.gameObject.AddComponent<RawImage>();
            _videoImage.raycastTarget = false;
            _videoImage.enabled = false;

            // 影片比例與螢幕不同時保持原比例、上下或左右留黑邊
            _videoFitter = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            _videoFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            _video = gameObject.AddComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.source = VideoSource.Url;
            _video.url = fullPath;
            _video.isLooping = false;
            _video.renderMode = VideoRenderMode.APIOnly;
            _video.audioOutputMode = VideoAudioOutputMode.Direct;
            _video.prepareCompleted += OnVideoPrepared;
            _video.errorReceived += OnVideoError;
            _video.loopPointReached += OnVideoEnded;

            _prepareDeadline = Time.unscaledTime + PrepareTimeoutSeconds;
            _video.Prepare();
            Debug.Log("[IntroPlayer] 播放開場影片：" + fullPath);
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (_finishing || source != _video)
            {
                return;
            }

            int width = source.width > 0 ? (int)source.width : (int)UiFactory.ReferenceResolution.x;
            int height = source.height > 0 ? (int)source.height : (int)UiFactory.ReferenceResolution.y;

            _videoTexture = new RenderTexture(width, height, 0);
            _videoTexture.Create();

            source.renderMode = VideoRenderMode.RenderTexture;
            source.targetTexture = _videoTexture;

            _videoImage.texture = _videoTexture;
            _videoImage.enabled = true;
            _videoFitter.aspectRatio = width / (float)height;

            source.Play();
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning("[IntroPlayer] 開場影片播放失敗，改播預設開場：" + message
                             + "（建議使用 H.264 編碼的 mp4）");
            FallBackToPlaceholder();
        }

        private void OnVideoEnded(VideoPlayer source)
        {
            // 停在最後一格；連線還沒好的話由 Update 顯示「連線準備中」繼續等
            source.Pause();
            if (_contentEndedAt < 0f)
            {
                _contentEndedAt = Time.unscaledTime;
            }
        }

        private void FallBackToPlaceholder()
        {
            ReleaseVideo();

            if (_videoImage != null)
            {
                Destroy(_videoImage.gameObject);
                _videoImage = null;
            }

            if (!_finishing && _placeholder == null)
            {
                StartPlaceholder();
            }
        }

        private void ReleaseVideo()
        {
            if (_video != null)
            {
                _video.prepareCompleted -= OnVideoPrepared;
                _video.errorReceived -= OnVideoError;
                _video.loopPointReached -= OnVideoEnded;
                _video.Stop();
                Destroy(_video);
                _video = null;
            }

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
                _videoTexture = null;
            }
        }

        // ---- 預設開場畫面（沒有影片時）----

        private void BuildCanvas()
        {
            Canvas canvas = UiFactory.CreateCanvas("IntroCanvas", CanvasSortOrder);
            canvas.transform.SetParent(transform, false);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();

            _root = UiFactory.Panel(canvas.transform, "Backdrop", BackdropColor);
            UiFactory.Stretch(_root, 0f, 0f, 0f, 0f);
        }

        private void StartPlaceholder()
        {
            _placeholder = UiFactory.Node(_root, "Placeholder");
            UiFactory.Stretch(_placeholder, 0f, 0f, 0f, 0f);

            BuildRunners();

            _title = UiFactory.Label(_placeholder, "Title", "賽馬場", 168,
                TextAnchor.MiddleCenter, TitleColor, FontStyle.Bold);
            UiFactory.Place((RectTransform)_title.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 110f), new Vector2(1400f, 220f));

            Text subtitle = UiFactory.Label(_placeholder, "Subtitle", "今晚的比賽即將開始", 46,
                TextAnchor.MiddleCenter, UiFactory.TextColor);
            UiFactory.Place((RectTransform)subtitle.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -10f), new Vector2(1400f, 70f));

            if (_config.IntroSkippable)
            {
                Text hint = UiFactory.Label(_placeholder, "SkipHint", "按空白鍵略過", 24,
                    TextAnchor.LowerRight, UiFactory.MutedTextColor);
                UiFactory.Place((RectTransform)hint.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-48f, 36f), new Vector2(400f, 36f));
            }

            _placeholderStartedAt = Time.unscaledTime;
            _placeholderEndsAt = _placeholderStartedAt + (float)_config.PlaceholderSeconds;
        }

        /// <summary>四條馬匹顏色的色帶在標題下方由左往右奔跑，速度各不相同。</summary>
        private void BuildRunners()
        {
            _runners = new RectTransform[_horseColors.Length];
            for (int lane = 0; lane < _horseColors.Length; lane++)
            {
                RectTransform runner = UiFactory.Panel(_placeholder, "Runner_" + lane, _horseColors[lane]);
                UiFactory.Place(runner, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(0f, -150f - lane * 34f), new Vector2(RunnerLength, 14f));
                _runners[lane] = runner;
            }
        }

        private const float RunnerLength = 360f;
        private const float RunnerTravel = 2300f;

        private void AnimatePlaceholder(float now)
        {
            float elapsed = now - _placeholderStartedAt;

            // 標題 1 秒內淡入並微微放大到定位
            float appear = Mathf.Clamp01(elapsed / 1.1f);
            float eased = 1f - (1f - appear) * (1f - appear);
            Color color = TitleColor;
            color.a = appear;
            _title.color = color;
            _title.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, eased);

            for (int lane = 0; lane < _runners.Length; lane++)
            {
                // 每條的起跑時間與圈速略有差異，看起來像在比賽
                float lap = 2.4f + 0.23f * ((lane * 7) % 4);
                float phase = Mathf.Repeat(elapsed - lane * 0.18f, lap) / lap;
                float x = -RunnerTravel * 0.5f + phase * (RunnerTravel + RunnerLength);
                _runners[lane].anchoredPosition = new Vector2(x, _runners[lane].anchoredPosition.y);
            }
        }

        private static Color[] ReadHorseColors(List<HorseConfig> roster)
        {
            int count = roster != null && roster.Count > 0 ? roster.Count : 1;
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                string hex = roster != null && i < roster.Count ? roster[i].ColorHex : null;
                colors[i] = MaterialLibrary.ParseHex(hex, TitleColor);
            }

            return colors;
        }
    }
}
