using System;
using System.Collections.Generic;
using System.Text;
using HorseRace.Core;

namespace HorseRace.Tests
{
    /// <summary>
    /// Core 的單元測試。刻意不引入測試框架：這樣一個 `dotnet run` 就能跑完，
    /// 不需要開 Unity，也不需要還原任何套件。
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // 某些終端不支援切換編碼，忽略即可，不影響測試結果
            }

            Console.WriteLine("=== HorseRace Core 測試 ===");
            Console.WriteLine();

            RandomTests();
            EngineTests();
            OddsTests();
            EffectTests();
            DriveTests();
            ConfigTests();
            GameLoopTests();

            Console.WriteLine();
            if (Failures.Count == 0)
            {
                Console.WriteLine("全部通過：" + _passed + " 項");
                return 0;
            }

            Console.WriteLine("通過 " + _passed + " 項，失敗 " + Failures.Count + " 項：");
            foreach (string failure in Failures)
            {
                Console.WriteLine("  - " + failure);
            }

            return 1;
        }

        // ---------------------------------------------------------------- 亂數

        private static void RandomTests()
        {
            Section("DeterministicRandom");

            DeterministicRandom a = new DeterministicRandom(1234);
            DeterministicRandom b = new DeterministicRandom(1234);
            bool identical = true;
            for (int i = 0; i < 1000; i++)
            {
                if (a.NextDouble() != b.NextDouble())
                {
                    identical = false;
                    break;
                }
            }

            Check("同 seed 產生相同序列", identical);

            DeterministicRandom source = new DeterministicRandom(99);
            for (int i = 0; i < 37; i++)
            {
                source.NextDouble();
            }

            DeterministicRandom copy = source.Clone();
            bool cloneMatches = true;
            for (int i = 0; i < 500; i++)
            {
                if (source.NextDouble() != copy.NextDouble())
                {
                    cloneMatches = false;
                    break;
                }
            }

            Check("Clone 後續序列與本體一致", cloneMatches);

            DeterministicRandom uniform = new DeterministicRandom(7);
            bool inRange = true;
            for (int i = 0; i < 100000; i++)
            {
                double value = uniform.NextDouble();
                if (value < 0.0 || value >= 1.0)
                {
                    inRange = false;
                    break;
                }
            }

            Check("NextDouble 落在 [0,1)", inRange);

            DeterministicRandom gaussianSource = new DeterministicRandom(2026);
            const int samples = 200000;
            double sum = 0.0;
            double sumSquares = 0.0;
            for (int i = 0; i < samples; i++)
            {
                double value = gaussianSource.NextGaussian();
                sum += value;
                sumSquares += value * value;
            }

            double mean = sum / samples;
            double variance = sumSquares / samples - mean * mean;
            Check("常態分布均值接近 0（實得 " + mean.ToString("F4") + "）", Math.Abs(mean) < 0.02);
            Check("常態分布標準差接近 1（實得 " + Math.Sqrt(variance).ToString("F4") + "）",
                Math.Abs(Math.Sqrt(variance) - 1.0) < 0.02);
        }

        // ---------------------------------------------------------------- 引擎

        private static void EngineTests()
        {
            Section("RaceEngine");

            GameConfig config = DefaultConfig();
            HorseConfig[] lineup = RaceLineup.Create(config.Race, config.Roster, 42);

            RaceEngine first = new RaceEngine(config.Race, lineup, 555);
            first.RunToCompletion();
            RaceEngine second = new RaceEngine(config.Race, lineup, 555);
            second.RunToCompletion();

            Check("同 seed 完賽時間完全相同", SameFinishTimes(first, second));
            Check("同 seed 名次完全相同", SameOrder(first.GetFinishOrder(), second.GetFinishOrder()));

            bool allAtLine = true;
            bool allFinished = true;
            for (int lane = 0; lane < first.HorseCount; lane++)
            {
                HorseState horse = first.Horses[lane];
                allFinished &= horse.Finished;
                allAtLine &= Math.Abs(horse.Distance - config.Race.TrackLengthMeters) < 1e-9;
            }

            Check("所有馬都完賽", allFinished);
            Check("完賽距離等於賽道長度", allAtLine);

            int[] order = first.GetFinishOrder();
            bool sortedByTime = true;
            bool ranksAssigned = true;
            for (int position = 1; position < order.Length; position++)
            {
                if (first.Horses[order[position - 1]].FinishTime > first.Horses[order[position]].FinishTime)
                {
                    sortedByTime = false;
                }
            }

            for (int position = 0; position < order.Length; position++)
            {
                if (first.Horses[order[position]].FinishRank != position + 1)
                {
                    ranksAssigned = false;
                }
            }

            Check("名次依完賽時間遞增", sortedByTime);
            Check("FinishRank 已正確寫回", ranksAssigned);
            Check("WinnerLane 等於名次第一", first.WinnerLane == order[0]);

            double raceSeconds = first.Horses[order[order.Length - 1]].FinishTime;
            Check("單場時長落在 15～45 秒的體感區間（實得 " + raceSeconds.ToString("F1") + " 秒）",
                raceSeconds > 15.0 && raceSeconds < 45.0);

            // 累加器：一次餵 0.2 秒應等於分成 10 次餵 0.02 秒
            RaceEngine chunked = new RaceEngine(config.Race, lineup, 777);
            RaceEngine stepped = new RaceEngine(config.Race, lineup, 777);
            for (int i = 0; i < 40; i++)
            {
                chunked.Advance(0.2);
                for (int k = 0; k < 10; k++)
                {
                    stepped.Advance(0.02);
                }
            }

            Check("累加器：分段推進與逐步推進結果一致", SameDistances(chunked, stepped));

            // 餵入零碎的時間片段，不該產生額外或遺漏的模擬步
            RaceEngine ragged = new RaceEngine(config.Race, lineup, 777);
            double fed = 0.0;
            DeterministicRandom jitter = new DeterministicRandom(3);
            while (fed < 8.0)
            {
                double slice = jitter.NextRange(0.001, 0.05);
                ragged.Advance(slice);
                fed += slice;
            }

            RaceEngine smooth = new RaceEngine(config.Race, lineup, 777);
            int wholeSteps = (int)(fed / config.Race.FixedStepSeconds);
            for (int i = 0; i < wholeSteps; i++)
            {
                smooth.Advance(config.Race.FixedStepSeconds);
            }

            Check("不規則幀距與等距推進走過相同步數",
                Math.Abs(ragged.ElapsedSeconds - smooth.ElapsedSeconds) < 1e-9);

            // Clone 的獨立性與一致性
            RaceEngine original = new RaceEngine(config.Race, lineup, 31337);
            original.Advance(5.0);
            RaceEngine branch = original.Clone();

            double distanceBeforeBranch = original.Horses[0].Distance;
            branch.Advance(3.0);
            Check("Clone 推進不影響本體",
                Math.Abs(original.Horses[0].Distance - distanceBeforeBranch) < 1e-12);

            original.Advance(3.0);
            Check("Clone 與本體推進相同時間後結果一致", SameDistances(original, branch));

            // 不同 seed 應該真的會跑出不同結果
            HashSet<int> winners = new HashSet<int>();
            for (int seed = 0; seed < 60; seed++)
            {
                RaceEngine engine = new RaceEngine(config.Race, lineup, seed * 101);
                engine.RunToCompletion();
                winners.Add(engine.WinnerLane);
            }

            Check("60 個 seed 至少跑出 3 種冠軍（實得 " + winners.Count + " 種）", winners.Count >= 3);

            // 波動為零的馬不該有任何雜訊
            HorseConfig steady = new HorseConfig
            {
                Name = "石頭",
                BaseSpeed = 17.0,
                Stamina = 1.0,
                Volatility = 0.0
            };
            RaceEngine steadyRace = new RaceEngine(config.Race, new[] { steady, steady.Clone() }, 5);
            steadyRace.Advance(4.0);
            Check("Volatility 為 0 時波動狀態維持 0",
                Math.Abs(steadyRace.Horses[0].Noise) < 1e-12);
        }

        // ---------------------------------------------------------------- 賠率

        private static void OddsTests()
        {
            Section("OddsCalculator");

            GameConfig config = DefaultConfig();
            config.Race.OddsSimulationRuns = 400; // 測試用，壓低次數換速度
            HorseConfig[] lineup = RaceLineup.Create(config.Race, config.Roster, 8);

            OddsResult result = OddsCalculator.ComputeDetailed(config.Race, lineup, 12345);

            bool withinBounds = true;
            double winRateSum = 0.0;
            for (int lane = 0; lane < lineup.Length; lane++)
            {
                withinBounds &= result.Odds[lane] >= config.Race.MinOdds - 1e-9
                                && result.Odds[lane] <= config.Race.MaxOdds + 1e-9;
                winRateSum += result.WinRates[lane];
            }

            Check("賠率落在設定的上下限內", withinBounds);
            Check("勝率總和為 1（實得 " + winRateSum.ToString("F4") + "）",
                Math.Abs(winRateSum - 1.0) < 1e-9);

            // 明顯較快的馬，賠率必須較低
            HorseConfig[] mixed =
            {
                new HorseConfig { Name = "快", BaseSpeed = 19.0, Stamina = 0.7, Volatility = 0.3 },
                new HorseConfig { Name = "慢", BaseSpeed = 15.5, Stamina = 0.5, Volatility = 0.3 }
            };
            double[] mixedOdds = OddsCalculator.Compute(config.Race, mixed, 999);
            Check("快馬的賠率低於慢馬（" + mixedOdds[0].ToString("F2") + " < "
                  + mixedOdds[1].ToString("F2") + "）", mixedOdds[0] < mixedOdds[1]);

            // 隱含機率總和 ≒ 1/(1-抽水率)，這是莊家優勢的數學定義
            double impliedSum = 0.0;
            for (int lane = 0; lane < lineup.Length; lane++)
            {
                impliedSum += 1.0 / result.Odds[lane];
            }

            double expected = 1.0 / (1.0 - config.Race.TakeRate);
            Check("隱含機率總和接近 1/(1-抽水率)（實得 " + impliedSum.ToString("F3")
                  + "，預期 " + expected.ToString("F3") + "）",
                Math.Abs(impliedSum - expected) < 0.08);

            Check("賠率計算不會改動傳入的名單",
                Math.Abs(mixed[0].BaseSpeed - 19.0) < 1e-12);

            // 這是「遊戲好不好玩」的回歸測試，不只是數學檢查。
            // 預設名冊四匹馬的實力差距只有約 3.5%，若隨機性不足，
            // 最快的那匹會幾乎每場都贏，賠率掉到 1.1 倍，下注就失去意義。
            GameConfig live = DefaultConfig();
            live.Race.OddsSimulationRuns = 1200;
            HorseConfig[] liveLineup = RaceLineup.Create(live.Race, live.Roster, 1234);
            OddsResult liveOdds = OddsCalculator.ComputeDetailed(live.Race, liveLineup, 555);

            double lowest = double.MaxValue;
            double highest = 0.0;
            string summary = "";
            for (int lane = 0; lane < liveLineup.Length; lane++)
            {
                lowest = Math.Min(lowest, liveOdds.Odds[lane]);
                highest = Math.Max(highest, liveOdds.Odds[lane]);
                summary += liveLineup[lane].Name + " " + liveOdds.Odds[lane].ToString("F2") + "  ";
            }

            Check("預設名冊的賠率落在 1.5～12 倍的可玩區間（" + summary.Trim() + "）",
                lowest >= 1.5 && highest <= 12.0);
        }

        // ---------------------------------------------------------------- 道具

        private static void EffectTests()
        {
            Section("SpeedEffect");

            GameConfig config = DefaultConfig();
            HorseConfig[] lineup = RaceLineup.Create(config.Race, config.Roster, 3);

            RaceEngine engine = new RaceEngine(config.Race, lineup, 100);
            AdvanceBySeconds(engine, 2.0);
            bool applied = engine.ApplyEffect(0, MakeEffect(EffectKind.Boost, 1.35, 1.0));
            Check("可對比賽中的馬施加效果", applied);
            Check("效果數量正確", engine.ActiveEffectCount(0) == 1);

            AdvanceBySeconds(engine, 0.5);
            Check("效果在持續時間內仍生效", engine.ActiveEffectCount(0) == 1);
            AdvanceBySeconds(engine, 0.6);
            Check("效果超過持續時間後自動移除", engine.ActiveEffectCount(0) == 0);

            Check("對不存在的閘號施加效果會被拒絕",
                !engine.ApplyEffect(99, MakeEffect(EffectKind.Boost, 1.35, 1.0)));
            Check("null 效果會被拒絕，不會拋例外", !engine.ApplyEffect(0, null));

            // 加速應該讓平均名次變好，減速則變差
            double plain = AverageRank(config, lineup, EffectKind.Boost, 1.0);
            double boosted = AverageRank(config, lineup, EffectKind.Boost, 1.35);
            double slowed = AverageRank(config, lineup, EffectKind.Slow, 0.60);

            Check("加速讓平均名次變好（" + boosted.ToString("F2") + " < " + plain.ToString("F2") + "）",
                boosted < plain);
            Check("減速讓平均名次變差（" + slowed.ToString("F2") + " > " + plain.ToString("F2") + "）",
                slowed > plain);

            RaceEngine finishedRace = new RaceEngine(config.Race, lineup, 100);
            finishedRace.RunToCompletion();
            Check("已完賽的馬不再吃效果",
                !finishedRace.ApplyEffect(0, MakeEffect(EffectKind.Slow, 0.6, 2.0)));
        }

        /// <summary>對 0 號馬在開賽 3 秒後施加指定效果，回傳多場的平均名次。</summary>
        private static double AverageRank(GameConfig config, HorseConfig[] lineup,
            EffectKind kind, double multiplier)
        {
            const int races = 240;
            int rankSum = 0;

            for (int i = 0; i < races; i++)
            {
                RaceEngine engine = new RaceEngine(config.Race, lineup, 4000 + i * 17);
                AdvanceBySeconds(engine, 3.0);
                if (Math.Abs(multiplier - 1.0) > 1e-9)
                {
                    engine.ApplyEffect(0, MakeEffect(kind, multiplier, 2.0));
                }

                engine.RunToCompletion();
                engine.GetFinishOrder();
                rankSum += engine.Horses[0].FinishRank;
            }

            return (double)rankSum / races;
        }

        private static SpeedEffect MakeEffect(EffectKind kind, double multiplier, double seconds)
        {
            return new SpeedEffect
            {
                Kind = kind,
                Multiplier = multiplier,
                RemainingSeconds = seconds,
                SourcePlayerId = "test",
                SourceNickname = "測試員"
            };
        }

        // ---------------------------------------------------------- 體力驅動

        private static void DriveTests()
        {
            Section("體力驅動（計步器／手機搖動）");

            GameConfig config = DefaultConfig();
            HorseConfig[] lineup = RaceLineup.Create(config.Race, config.Roster, 21);
            double fullRate = config.Race.StepsPerSecondForFullDrive;

            RaceEngine engine = new RaceEngine(config.Race, lineup, 500);
            Check("開賽時驅動強度為 0", Math.Abs(engine.DriveLevelOf(0)) < 1e-12);

            engine.AddSteps(0, 3);
            Check("累加步數會提升驅動強度", engine.DriveLevelOf(0) > 0.0);

            // 以全力步頻持續餵入，強度應收斂到接近上限
            RaceEngine atFullRate = new RaceEngine(config.Race, lineup, 500);
            DriveFor(atFullRate, 0, 5.0, fullRate);
            double fullLevel = atFullRate.DriveLevelOf(0);
            Check("以全力步頻持續搖，強度收斂到接近 1（實得 " + fullLevel.ToString("F2") + "）",
                fullLevel > 0.85);

            // 半速應該落在中段，證明強度真的反映步頻而不是只有開關兩種狀態
            RaceEngine atHalfRate = new RaceEngine(config.Race, lineup, 500);
            DriveFor(atHalfRate, 0, 5.0, fullRate * 0.5);
            double halfLevel = atHalfRate.DriveLevelOf(0);
            Check("半速搖動的強度落在中段（實得 " + halfLevel.ToString("F2") + "）",
                halfLevel > 0.3 && halfLevel < 0.75);

            // 狂甩不能突破上限
            RaceEngine spammed = new RaceEngine(config.Race, lineup, 500);
            spammed.AddSteps(0, 100000);
            Check("狂甩不會讓強度超過 1", spammed.DriveLevelOf(0) <= 1.0 + 1e-12);

            // 停止搖動後要自己滑回去，這同時是裝置沒電／斷線的容錯行為
            RaceEngine coasting = new RaceEngine(config.Race, lineup, 500);
            DriveFor(coasting, 0, 3.0, fullRate);
            AdvanceBySeconds(coasting, 6.0);
            Check("停止搖動後強度衰減回接近 0（實得 "
                  + coasting.DriveLevelOf(0).ToString("F3") + "）",
                coasting.DriveLevelOf(0) < 0.02);

            Check("無效閘號會被拒絕", !engine.AddSteps(99, 5));
            Check("零或負步數會被拒絕", !engine.AddSteps(0, 0) && !engine.AddSteps(0, -3));

            RaceEngine finished = new RaceEngine(config.Race, lineup, 500);
            finished.RunToCompletion();
            Check("已完賽的馬不再接受步數", !finished.AddSteps(0, 10));

            RaceEngine source = new RaceEngine(config.Race, lineup, 501);
            DriveFor(source, 0, 2.0, fullRate);
            RaceEngine copy = source.Clone();
            Check("Clone 保留驅動強度",
                Math.Abs(copy.DriveLevelOf(0) - source.DriveLevelOf(0)) < 1e-12);

            // 真正該驗的事：一直搖到底的馬，名次要明顯變好
            double idleRank = AverageRankWithDrive(config, lineup, 0.0);
            double drivenRank = AverageRankWithDrive(config, lineup, fullRate);
            Check("持續搖動讓平均名次明顯變好（" + drivenRank.ToString("F2")
                  + " < " + idleRank.ToString("F2") + "）",
                drivenRank < idleRank - 0.4);

            // 上限旋鈕轉到 0 時，搖動必須完全失效
            GameConfig noBonus = DefaultConfig();
            noBonus.Race.MaxDriveBonus = 0.0;
            HorseConfig[] noBonusLineup = RaceLineup.Create(noBonus.Race, noBonus.Roster, 21);

            RaceEngine withoutDrive = new RaceEngine(noBonus.Race, noBonusLineup, 909);
            AdvanceBySeconds(withoutDrive, 6.0);
            RaceEngine withDrive = new RaceEngine(noBonus.Race, noBonusLineup, 909);
            DriveFor(withDrive, 0, 6.0, fullRate);

            Check("MaxDriveBonus 為 0 時搖動完全不影響賽況",
                Math.Abs(withDrive.Horses[0].Distance - withoutDrive.Horses[0].Distance) < 1e-9);
        }

        /// <summary>以指定步頻邊餵步數邊推進，模擬實際裝置持續回報的情形。</summary>
        private static void DriveFor(RaceEngine engine, int lane, double seconds, double stepsPerSecond)
        {
            const double slice = 0.02;
            int ticks = (int)Math.Round(seconds / slice);
            double pending = 0.0;

            for (int i = 0; i < ticks; i++)
            {
                pending += stepsPerSecond * slice;
                int whole = (int)pending;
                if (whole > 0)
                {
                    engine.AddSteps(lane, whole);
                    pending -= whole;
                }

                engine.Advance(slice);
            }
        }

        /// <summary>0 號馬全程以指定步頻搖動，回傳多場的平均名次。</summary>
        private static double AverageRankWithDrive(
            GameConfig config, HorseConfig[] lineup, double stepsPerSecond)
        {
            const int races = 120;
            int rankSum = 0;

            for (int i = 0; i < races; i++)
            {
                RaceEngine engine = new RaceEngine(config.Race, lineup, 6000 + i * 23);
                while (!engine.IsFinished && engine.ElapsedSeconds < config.Race.MaxRaceSeconds)
                {
                    DriveFor(engine, 0, 0.5, stepsPerSecond);
                }

                engine.GetFinishOrder();
                rankSum += engine.Horses[0].FinishRank;
            }

            return (double)rankSum / races;
        }

        // ---------------------------------------------------------------- 設定

        private static void ConfigTests()
        {
            Section("Config");

            RaceConfig broken = new RaceConfig
            {
                TrackLengthMeters = -50.0,
                FixedStepSeconds = 0.0,
                TakeRate = 9.9,
                OddsSimulationRuns = -1,
                MinOdds = 5.0,
                MaxOdds = 1.0,
                FatigueStart = 2.0
            };
            broken.Validate();

            Check("賽道長度被夾到下限", broken.TrackLengthMeters >= 50.0);
            Check("步長被夾到有效範圍", broken.FixedStepSeconds > 0.0);
            Check("抽水率被夾到 0.5 以內", broken.TakeRate <= 0.5);
            Check("模擬次數被夾到正值", broken.OddsSimulationRuns > 0);
            Check("賠率上限不會低於下限", broken.MaxOdds >= broken.MinOdds);
            Check("疲勞起點不會等於或超過 1", broken.FatigueStart < 1.0);

            GameConfig emptyRoster = new GameConfig();
            emptyRoster.Validate();
            Check("空名冊會退回預設四匹馬", emptyRoster.Roster.Count == 4);

            GameConfig withNulls = new GameConfig();
            withNulls.Roster.Add(null);
            withNulls.Roster.Add(new HorseConfig { Name = "唯一", BaseSpeed = 17.0 });
            withNulls.Validate();
            Check("名冊中的 null 會被清掉且不足時退回預設", withNulls.Roster.Count == 4);

            GameConfig oversized = GameConfig.CreateDefault();
            for (int i = 0; i < 12; i++)
            {
                oversized.Roster.Add(new HorseConfig { Name = "多餘" + i });
            }

            oversized.Validate();
            Check("名冊超過上限會被裁掉", oversized.Roster.Count == GameConfig.MaxHorses);

            HorseConfig badHorse = new HorseConfig
            {
                Name = null,
                ColorHex = "紅色",
                BaseSpeed = 999.0,
                Stamina = -3.0,
                Volatility = 42.0
            };
            badHorse.Validate();
            Check("壞掉的馬匹設定會被修正",
                !string.IsNullOrEmpty(badHorse.Name)
                && badHorse.ColorHex[0] == '#'
                && badHorse.BaseSpeed <= 60.0
                && badHorse.Stamina >= 0.0
                && badHorse.Volatility <= 1.0);

            ItemConfig brokenItems = new ItemConfig
            {
                BoostMultiplier = 0.5,
                SlowMultiplier = 5.0,
                DurationSeconds = -1.0,
                UsesPerRace = -5,
                MaxStacksPerHorse = 0
            };
            brokenItems.Validate();

            Check("加速倍率一定大於 1", brokenItems.BoostMultiplier > 1.0);
            Check("減速倍率一定小於 1", brokenItems.SlowMultiplier < 1.0);
            Check("道具持續時間被夾到正值", brokenItems.DurationSeconds > 0.0);
            Check("道具使用次數不會是負數", brokenItems.UsesPerRace >= 0);
            Check("道具疊加上限至少為 1", brokenItems.MaxStacksPerHorse >= 1);
            Check("MultiplierFor 對應到正確的倍率",
                Math.Abs(brokenItems.MultiplierFor(EffectKind.Boost) - brokenItems.BoostMultiplier) < 1e-12
                && Math.Abs(brokenItems.MultiplierFor(EffectKind.Slow) - brokenItems.SlowMultiplier) < 1e-12);

            GameConfig missingItems = GameConfig.CreateDefault();
            missingItems.Items = null;
            missingItems.Validate();
            Check("Items 為 null 時會補回預設", missingItems.Items != null);

            GameConfig jitterSource = DefaultConfig();
            HorseConfig[] lineupA = RaceLineup.Create(jitterSource.Race, jitterSource.Roster, 77);
            HorseConfig[] lineupB = RaceLineup.Create(jitterSource.Race, jitterSource.Roster, 78);
            Check("不同 seed 的出賽名單能力值不同",
                Math.Abs(lineupA[0].BaseSpeed - lineupB[0].BaseSpeed) > 1e-9);
            Check("產生名單不會改動原始名冊",
                Math.Abs(jitterSource.Roster[0].BaseSpeed - 17.4) < 1e-9);
        }

        // ---------------------------------------------------------------- 狀態機

        private static void GameLoopTests()
        {
            Section("GameLoop");

            GameConfig config = DefaultConfig();
            config.Race.IdleSeconds = 1.0;
            config.Race.BettingSeconds = 2.0;
            config.Race.PhotoSeconds = 1.0;
            config.Race.SettleSeconds = 1.0;

            List<RacePhase> visited = new List<RacePhase>();
            GameLoop loop = new GameLoop(config, 2026);
            loop.PhaseEntered += visited.Add;

            Check("起始階段為 Idle", loop.Phase == RacePhase.Idle);
            Check("起始就備妥出賽名單", loop.Lineup != null && loop.Lineup.Length == 4);
            Check("起始時賠率未算，NeedsOdds 為 true", loop.NeedsOdds);

            loop.SetOdds(loop.RaceNumber, new[] { 2.0, 3.0, 4.0, 5.0 });
            Check("注入賠率後 NeedsOdds 轉為 false", !loop.NeedsOdds);
            loop.SetOdds(loop.RaceNumber + 5, new[] { 9.0, 9.0, 9.0, 9.0 });
            Check("場次不符的賠率會被忽略", Math.Abs(loop.Odds[0] - 2.0) < 1e-9);
            loop.SetOdds(loop.RaceNumber, new[] { 1.0, 2.0 });
            Check("長度不符的賠率會被忽略", loop.Odds.Length == 4);

            // 跑完整整一輪
            double elapsed = 0.0;
            while (loop.RaceNumber == 1 && elapsed < 300.0)
            {
                loop.Tick(0.05);
                elapsed += 0.05;
            }

            Check("完整跑完一輪後進入第 2 場", loop.RaceNumber == 2);
            Check("階段依 Betting→Racing→Photo→Settle→Idle 前進",
                visited.Count >= 5
                && visited[0] == RacePhase.Betting
                && visited[1] == RacePhase.Racing
                && visited[2] == RacePhase.Photo
                && visited[3] == RacePhase.Settle
                && visited[4] == RacePhase.Idle);
            Check("換場後賠率被清空", loop.NeedsOdds);
            Check("換場後名單重新產生", loop.Lineup != null && loop.Race == null);

            // SkipPhase 應該能在 Racing 階段直接把比賽結束掉
            GameLoop skipper = new GameLoop(config, 99);
            skipper.SkipPhase(); // Idle -> Betting
            skipper.SkipPhase(); // Betting -> Racing
            Check("兩次 SkipPhase 後進入 Racing", skipper.Phase == RacePhase.Racing);
            Check("進入 Racing 時賽事已建立", skipper.Race != null);
            skipper.SkipPhase(); // Racing -> Photo
            Check("Racing 階段 SkipPhase 會跑完比賽", skipper.Phase == RacePhase.Photo);
            Check("比賽結束後名次已產生",
                skipper.FinishOrder != null && skipper.FinishOrder.Length == 4);

            GameLoop deterministicA = new GameLoop(config, 4242);
            GameLoop deterministicB = new GameLoop(config, 4242);
            Check("同 seed 的 GameLoop 產生相同名單與賽事種子",
                Math.Abs(deterministicA.Lineup[0].BaseSpeed - deterministicB.Lineup[0].BaseSpeed) < 1e-12
                && deterministicA.RaceSeed == deterministicB.RaceSeed);
        }

        // ---------------------------------------------------------------- 工具

        private static GameConfig DefaultConfig()
        {
            GameConfig config = GameConfig.CreateDefault();
            config.Validate();
            return config;
        }

        private static bool SameFinishTimes(RaceEngine a, RaceEngine b)
        {
            if (a.HorseCount != b.HorseCount)
            {
                return false;
            }

            for (int lane = 0; lane < a.HorseCount; lane++)
            {
                if (Math.Abs(a.Horses[lane].FinishTime - b.Horses[lane].FinishTime) > 1e-12)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameDistances(RaceEngine a, RaceEngine b)
        {
            if (a.HorseCount != b.HorseCount)
            {
                return false;
            }

            for (int lane = 0; lane < a.HorseCount; lane++)
            {
                if (Math.Abs(a.Horses[lane].Distance - b.Horses[lane].Distance) > 1e-12)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameOrder(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 以小片段推進指定秒數。單次 Advance 有 MaxCatchUpSeconds 的防卡頓上限，
        /// 想確實走完一段時間就必須分次餵，這也才貼近實際每幀呼叫的情形。
        /// </summary>
        private static void AdvanceBySeconds(RaceEngine engine, double seconds)
        {
            const double slice = 0.02;
            int steps = (int)Math.Round(seconds / slice);
            for (int i = 0; i < steps; i++)
            {
                engine.Advance(slice);
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine("-- " + title);
        }

        private static void Check(string description, bool condition)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("   [OK] " + description);
            }
            else
            {
                Failures.Add(description);
                Console.WriteLine("   [XX] " + description);
            }
        }
    }
}
