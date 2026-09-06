using System;
using System.Threading.Tasks;
using HorseRace.Core;
using HorseRace.Core.Protocol;
using HorseRace.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorseRace.View
{
    /// <summary>
    /// 大螢幕的總控。負責把 Core 的模擬結果搬到畫面上，以及把時間餵給 Core。
    /// 這一層不做任何遊戲判斷——階段、賽果、賠率全部問 <see cref="GameLoop"/>。
    /// </summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        /// <summary>閘內待機時的踱步幅度。給一點就好，不然看起來像在原地狂奔。</summary>
        private const float IdleStrideRatio = 0.12f;

        private static readonly Color SkyColor = new Color(0.52f, 0.70f, 0.87f);
        private static readonly Color BoostAuraColor = new Color(1f, 0.72f, 0.20f);
        private static readonly Color SlowAuraColor = new Color(0.36f, 0.68f, 1f);
        private static readonly Color DriveAuraLowColor = new Color(0.30f, 0.85f, 0.45f);
        private static readonly Color DriveAuraHighColor = new Color(1f, 0.35f, 0.15f);

        private GameConfig _config;
        private GameLoop _loop;
        private DeterministicRandom _debugRandom;

        private Camera _camera;
        private RaceCameraRig _cameraRig;
        private Transform _track;
        private GameObject _hudObject;
        private RaceHud _hud;
        private HorseView[] _horseViews;
        private int[] _liveRanks;

        private Task<double[]> _oddsTask;
        private int _oddsRaceNumber;
        private float _oddsStartedAt;

        private RelayClient _relay;
        private DriveMessage _driveMessage;
        private float _nextDrivePushTime;

        private void Awake()
        {
            Application.targetFrameRate = 60;

            _config = ConfigLoader.Load();
            DebugCapture.AttachIfRequested(gameObject);
            StartNewSession();
            StartRelay();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            HandleDebugInput();
            PumpOddsCalculation();
            PumpRelayInbox();

            _loop.Tick(deltaTime);

            UpdateHorses(deltaTime);
            UpdateCamera();
            UpdateHud();
            PushDriveSnapshot();
        }

        private void OnDestroy()
        {
            if (_loop != null)
            {
                _loop.PhaseEntered -= OnPhaseEntered;
            }

            if (_relay != null)
            {
                _relay.Dispose();
                _relay = null;
            }
        }

        // ---- 場次控制 ----

        private void StartNewSession()
        {
            int seed = unchecked((int)DateTime.Now.Ticks);

            _loop = new GameLoop(_config, seed);
            _loop.PhaseEntered += OnPhaseEntered;
            _debugRandom = new DeterministicRandom(seed ^ 0x5F3759D);
            _oddsTask = null;

            BuildScene();

            // GameLoop 在建構時就已經進入 Idle，那一刻還沒有訂閱者，所以補呼叫一次
            OnPhaseEntered(_loop.Phase);
        }

        /// <summary>重新開始。順便重讀設定檔，現場調完參數按 R 就生效，不必重開程式。</summary>
        private void Restart()
        {
            _loop.PhaseEntered -= OnPhaseEntered;
            TeardownScene();

            _config = ConfigLoader.Load();
            StartNewSession();
        }

        private void OnPhaseEntered(RacePhase phase)
        {
            // 現場出問題時，第一件事就是確認程式以為自己在哪個階段，所以這行留在正式版
            Debug.Log("[RaceDirector] 第 " + _loop.RaceNumber + " 場進入 " + phase + " 階段。");

            switch (phase)
            {
                case RacePhase.Idle:
                    _hud.SetLineup(_loop.Lineup);
                    _hud.HideResult();
                    ResetHorsesToGate();
                    _cameraRig.FrameGate();
                    _cameraRig.SnapToTarget();
                    break;

                case RacePhase.Racing:
                    _hud.HideResult();
                    break;

                case RacePhase.Photo:
                    _hud.ShowResult(_loop.Race, _loop.FinishOrder);
                    LogFinishOrder();
                    break;
            }

            BroadcastPhase();
        }

        /// <summary>把賽果寫進 log。賽後有人質疑名次時，這是唯一的客觀紀錄。</summary>
        private void LogFinishOrder()
        {
            int[] order = _loop.FinishOrder;
            if (order == null)
            {
                return;
            }

            System.Text.StringBuilder line = new System.Text.StringBuilder();
            line.Append("[RaceDirector] 第 ").Append(_loop.RaceNumber).Append(" 場結果：");

            for (int position = 0; position < order.Length; position++)
            {
                int lane = order[position];
                line.Append(position + 1).Append('.')
                    .Append(_loop.Lineup[lane].Name)
                    .Append("(閘").Append(lane).Append(") ")
                    .Append(_loop.Race.Horses[lane].FinishTime.ToString("F2")).Append("秒  ");
            }

            Debug.Log(line.ToString());
        }

        // ---- 中繼伺服器 ----

        private void StartRelay()
        {
            NetworkConfig network = _config.Network;
            if (!network.AutoConnect)
            {
                Debug.Log("[RaceDirector] 設定為不自動連線，以單機模式執行。");
                return;
            }

            _relay = new RelayClient();
            _relay.Start(network.RelayUrl, network.ReconnectSeconds);
            Debug.Log("[RaceDirector] 連往中繼伺服器：" + network.RelayUrl);
        }

        /// <summary>
        /// 在主執行緒消化背景收到的訊息。
        /// 一則壞封包只記 log 並跳過，絕不能讓賽事中斷——這條路徑上的輸入來自現場的手機。
        /// </summary>
        private void PumpRelayInbox()
        {
            if (_relay == null)
            {
                return;
            }

            string payload;
            while (_relay.TryDequeue(out payload))
            {
                InboundMessage message;
                try
                {
                    message = JsonUtility.FromJson<InboundMessage>(payload);
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[RaceDirector] 無法解析手機訊息，已丟棄："
                                     + error.GetType().Name + " - " + error.Message);
                    continue;
                }

                if (message == null || string.IsNullOrEmpty(message.t))
                {
                    continue;
                }

                HandleInbound(message);
            }
        }

        private void HandleInbound(InboundMessage message)
        {
            switch (message.t)
            {
                case MessageType.Step:
                    // 只有比賽進行中才吃步數；引擎自己會擋掉無效閘號與已完賽的馬
                    if (_loop.Phase == RacePhase.Racing && _loop.Race != null)
                    {
                        _loop.Race.AddSteps(message.lane, message.n);
                    }

                    break;

                case MessageType.Join:
                    // 目前只需要讓手機拿到目前階段與名單，之後 M4 會在這裡建立玩家錢包
                    BroadcastPhase();
                    break;
            }
        }

        private void BroadcastPhase()
        {
            if (_relay == null || !_relay.IsConnected || _loop.Lineup == null)
            {
                return;
            }

            HorseConfig[] lineup = _loop.Lineup;
            double[] odds = _loop.Odds;

            HorseInfo[] horses = new HorseInfo[lineup.Length];
            for (int lane = 0; lane < lineup.Length; lane++)
            {
                horses[lane] = new HorseInfo
                {
                    id = lane,
                    name = lineup[lane].Name,
                    color = lineup[lane].ColorHex,
                    odds = odds != null && lane < odds.Length ? odds[lane] : 0.0
                };
            }

            // 傳結束時間戳而非剩餘秒數，讓手機自行遞減，避免網路抖動造成秒數跳動
            long endsAt = 0;
            if (_loop.Phase != RacePhase.Racing)
            {
                endsAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                         + (long)(_loop.PhaseRemainingSeconds * 1000.0);
            }

            PhaseMessage message = new PhaseMessage
            {
                phase = _loop.Phase.ToString().ToLowerInvariant(),
                endsAt = endsAt,
                race = _loop.RaceNumber,
                horses = horses
            };

            _relay.Send(JsonUtility.ToJson(message));
        }

        /// <summary>把各匹馬的體力驅動強度推回手機，讓搖的人看得到自己的效果。</summary>
        private void PushDriveSnapshot()
        {
            if (_relay == null || !_relay.IsConnected
                || _loop.Phase != RacePhase.Racing || _loop.Race == null)
            {
                return;
            }

            if (Time.time < _nextDrivePushTime)
            {
                return;
            }

            _nextDrivePushTime = Time.time + (float)(1.0 / _config.Network.SnapshotsPerSecond);

            RaceEngine race = _loop.Race;
            if (_driveMessage == null || _driveMessage.d == null
                || _driveMessage.d.Length != race.HorseCount)
            {
                _driveMessage = new DriveMessage { d = new float[race.HorseCount] };
            }

            for (int lane = 0; lane < race.HorseCount; lane++)
            {
                _driveMessage.d[lane] = (float)race.DriveLevelOf(lane);
            }

            _relay.Send(JsonUtility.ToJson(_driveMessage));
        }

        // ---- 賠率（背景計算）----

        private void PumpOddsCalculation()
        {
            if (_oddsTask == null)
            {
                if (_loop.NeedsOdds)
                {
                    StartOddsCalculation();
                }

                return;
            }

            if (!_oddsTask.IsCompleted)
            {
                return;
            }

            if (_oddsTask.IsFaulted)
            {
                Debug.LogError("[RaceDirector] 賠率計算失敗，改用平均賠率。原因："
                               + DescribeTaskError(_oddsTask));
                _loop.SetOdds(_oddsRaceNumber, BuildFallbackOdds());
            }
            else if (!_oddsTask.IsCanceled)
            {
                _loop.SetOdds(_oddsRaceNumber, _oddsTask.Result);

                // 記下耗時：這個數字若接近或超過 IdleSeconds，
                // 下注階段一開始會有短暫的「計算中」，該調長待機時間或調低模擬次數
                float elapsedMs = (Time.realtimeSinceStartup - _oddsStartedAt) * 1000f;
                Debug.Log("[RaceDirector] 第 " + _oddsRaceNumber + " 場賠率計算完成，耗時 "
                          + Mathf.RoundToInt(elapsedMs) + " ms（模擬 "
                          + _config.Race.OddsSimulationRuns + " 場）。");
            }

            _oddsTask = null;

            // 賠率是在進入 Idle 之後才算完的，要補播一次，手機才看得到數字
            BroadcastPhase();
        }

        /// <summary>
        /// 蒙地卡羅要跑上千場模擬，同步跑會讓畫面卡一下，所以丟到背景執行緒。
        /// 傳進去的設定是複本、名單在本場結束前不會被改動，因此沒有共用狀態的問題。
        /// </summary>
        private void StartOddsCalculation()
        {
            RaceConfig configSnapshot = _config.Race.Clone();
            HorseConfig[] lineup = _loop.Lineup;
            int seed = _loop.RaceSeed;

            _oddsRaceNumber = _loop.RaceNumber;
            _oddsStartedAt = Time.realtimeSinceStartup;
            _oddsTask = Task.Run(() => OddsCalculator.Compute(configSnapshot, lineup, seed));
        }

        private double[] BuildFallbackOdds()
        {
            int count = _loop.Lineup.Length;
            double[] odds = new double[count];
            double flat = (1.0 - _config.Race.TakeRate) * count;

            for (int lane = 0; lane < count; lane++)
            {
                odds[lane] = Math.Round(flat, 2);
            }

            return odds;
        }

        private static string DescribeTaskError(Task task)
        {
            if (task.Exception == null)
            {
                return "未知錯誤";
            }

            Exception inner = task.Exception.GetBaseException();
            return inner.GetType().Name + " - " + inner.Message;
        }

        // ---- 畫面更新 ----

        private void UpdateHorses(float deltaTime)
        {
            RaceEngine race = _loop.Race;

            if (race == null)
            {
                for (int lane = 0; lane < _horseViews.Length; lane++)
                {
                    _horseViews[lane].UpdateVisual(0f, IdleStrideRatio, deltaTime);
                }

                return;
            }

            for (int lane = 0; lane < _horseViews.Length && lane < race.HorseCount; lane++)
            {
                HorseState horse = race.Horses[lane];
                float speedRatio = (float)(horse.Speed / horse.Config.BaseSpeed);

                _horseViews[lane].UpdateVisual((float)horse.Progress01, speedRatio, deltaTime);
                UpdateAura(_horseViews[lane], horse);
            }
        }

        /// <summary>光環優先序：道具 &gt; 體力驅動。道具是別人動的手腳，比自己出力更需要被看見。</summary>
        private static void UpdateAura(HorseView view, HorseState horse)
        {
            int effectCount = horse.Effects.Count;
            if (effectCount > 0)
            {
                EffectKind kind = horse.Effects[effectCount - 1].Kind;
                view.SetAura(true, kind == EffectKind.Boost ? BoostAuraColor : SlowAuraColor);
                return;
            }

            if (horse.DriveLevel > DriveAuraThreshold)
            {
                // 搖得越用力光環越亮，讓大螢幕上看得出誰在拚
                float intensity = Mathf.InverseLerp(DriveAuraThreshold, 1f, (float)horse.DriveLevel);
                view.SetAura(true, Color.Lerp(DriveAuraLowColor, DriveAuraHighColor, intensity));
                return;
            }

            view.SetAura(false, Color.white);
        }

        /// <summary>低於這個驅動強度就不顯示光環，免得整場都亮著反而看不出差別。</summary>
        private const float DriveAuraThreshold = 0.25f;

        private void UpdateCamera()
        {
            switch (_loop.Phase)
            {
                case RacePhase.Idle:
                case RacePhase.Betting:
                    _cameraRig.FrameGate();
                    break;

                case RacePhase.Racing:
                    // 鏡頭需要的是頭尾兩端，不是平均值——它要框住的是整個馬群
                    float leader = 0f;
                    float trailer = 1f;
                    RaceEngine race = _loop.Race;

                    for (int lane = 0; lane < race.HorseCount; lane++)
                    {
                        float progress = (float)race.Horses[lane].Progress01;
                        if (progress > leader)
                        {
                            leader = progress;
                        }

                        if (progress < trailer)
                        {
                            trailer = progress;
                        }
                    }

                    _cameraRig.FollowPack(leader, trailer);
                    break;

                default:
                    _cameraRig.FramePhotoFinish();
                    break;
            }
        }

        private void UpdateHud()
        {
            _hud.ShowPhase(_loop.Phase, _loop.PhaseRemainingSeconds, _loop.RaceNumber);

            if (_loop.Race == null)
            {
                _hud.ShowOdds(_loop.Odds);
            }
            else
            {
                _loop.Race.FillLiveRanks(_liveRanks);
                _hud.ShowLiveRanks(_loop.Race, _liveRanks);
            }

            // 衝線與結算階段關掉名牌：四匹馬擠在終點，名牌會互相重疊，
            // 而名次面板本來就把名字全列出來了，留著只是把畫面弄亂
            bool showNameTags = _loop.Phase != RacePhase.Photo && _loop.Phase != RacePhase.Settle;
            _hud.UpdateNameTags(_horseViews, _camera, showNameTags);

            _hud.SetConnectionStatus(
                _relay != null && _relay.IsConnected,
                _relay == null ? "單機模式" : _relay.StatusText);
        }

        private void ResetHorsesToGate()
        {
            for (int lane = 0; lane < _horseViews.Length; lane++)
            {
                _horseViews[lane].ResetToGate();
            }
        }

        // ---- 場景建置 ----

        private void BuildScene()
        {
            int laneCount = _config.Roster.Count;

            SetupEnvironment();

            _track = TrackBuilder.Build(laneCount);
            _horseViews = new HorseView[laneCount];
            for (int lane = 0; lane < laneCount; lane++)
            {
                _horseViews[lane] = HorseView.Create(_track, lane, laneCount, _loop.Lineup[lane]);
            }

            _liveRanks = new int[laneCount];

            _hudObject = new GameObject("Hud");
            _hudObject.transform.SetParent(transform, false);
            _hud = _hudObject.AddComponent<RaceHud>();
            _hud.Build(laneCount);

            if (_cameraRig == null)
            {
                _cameraRig = gameObject.AddComponent<RaceCameraRig>();
            }

            _cameraRig.Configure(_camera, laneCount);
        }

        private void TeardownScene()
        {
            if (_track != null)
            {
                // 先關掉再銷毀：Destroy 要到影格結束才生效，否則這一幀會看到兩座賽道疊在一起
                _track.gameObject.SetActive(false);
                Destroy(_track.gameObject);
                _track = null;
            }

            if (_hudObject != null)
            {
                _hudObject.SetActive(false);
                Destroy(_hudObject);
                _hudObject = null;
                _hud = null;
            }

            _horseViews = null;
        }

        private void SetupEnvironment()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                _camera = cameraObject.AddComponent<Camera>();
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = SkyColor;

            Light sun = FindObjectOfType<Light>();
            if (sun == null)
            {
                GameObject lightObject = new GameObject("Sun");
                sun = lightObject.AddComponent<Light>();
            }

            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.90f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            // 從看台側後方打下來，馬的影子會落在鏡頭這一側，比較容易判斷前後
            sun.transform.rotation = Quaternion.Euler(52f, 200f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.49f, 0.56f);
        }

        // ---- 除錯輸入 ----

        private void HandleDebugInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _loop.SkipPhase();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Restart();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ApplyDebugEffect(EffectKind.Boost);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ApplyDebugEffect(EffectKind.Slow);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
        }

        /// <summary>
        /// 除錯用：對隨機一匹馬丟道具。M5 接上手機端之後，走的會是同一條 ApplyEffect 路徑。
        /// </summary>
        private void ApplyDebugEffect(EffectKind kind)
        {
            RaceEngine race = _loop.Race;
            if (_loop.Phase != RacePhase.Racing || race == null)
            {
                return;
            }

            int lane = _debugRandom.NextInt(race.HorseCount);
            ItemConfig items = _config.Items;

            race.ApplyEffect(lane, new SpeedEffect
            {
                Kind = kind,
                Multiplier = items.MultiplierFor(kind),
                RemainingSeconds = items.DurationSeconds,
                SourcePlayerId = "debug",
                SourceNickname = "測試"
            });
        }
    }
}
