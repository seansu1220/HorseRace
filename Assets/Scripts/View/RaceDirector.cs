using System;
using System.Threading.Tasks;
using HorseRace.Core;
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

        private void Awake()
        {
            Application.targetFrameRate = 60;

            _config = ConfigLoader.Load();
            DebugCapture.AttachIfRequested(gameObject);
            StartNewSession();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            HandleDebugInput();
            PumpOddsCalculation();

            _loop.Tick(deltaTime);

            UpdateHorses(deltaTime);
            UpdateCamera();
            UpdateHud();
        }

        private void OnDestroy()
        {
            if (_loop != null)
            {
                _loop.PhaseEntered -= OnPhaseEntered;
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
                    break;
            }
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
            }

            _oddsTask = null;
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

        private static void UpdateAura(HorseView view, HorseState horse)
        {
            int effectCount = horse.Effects.Count;
            if (effectCount == 0)
            {
                view.SetAura(false, Color.white);
                return;
            }

            // 以最後施加的效果決定光環顏色，玩家最在意的就是「剛剛誰動了手腳」
            EffectKind kind = horse.Effects[effectCount - 1].Kind;
            view.SetAura(true, kind == EffectKind.Boost ? BoostAuraColor : SlowAuraColor);
        }

        private void UpdateCamera()
        {
            switch (_loop.Phase)
            {
                case RacePhase.Idle:
                case RacePhase.Betting:
                    _cameraRig.FrameGate();
                    break;

                case RacePhase.Racing:
                    float leader = 0f;
                    float total = 0f;
                    RaceEngine race = _loop.Race;

                    for (int lane = 0; lane < race.HorseCount; lane++)
                    {
                        float progress = (float)race.Horses[lane].Progress01;
                        total += progress;
                        if (progress > leader)
                        {
                            leader = progress;
                        }
                    }

                    _cameraRig.FollowPack(leader, total / race.HorseCount);
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
