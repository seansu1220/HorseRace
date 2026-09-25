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
            BettingTests();
            ConfigTests();
            GameLoopTests();
            LobbyTests();
            ItemShopTests();
            StepGateTests();

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

        // ---------------------------------------------------------- 下注與籌碼

        private static void BettingTests()
        {
            Section("BettingBook");

            GameConfig config = DefaultConfig();
            RaceConfig race = config.Race;
            BettingBook book = new BettingBook(race);

            PlayerAccount ming = book.Join("p1", "阿明");
            Check("新玩家拿到初始籌碼", ming.Balance == race.StartingChips);
            Check("玩家數正確", book.PlayerCount == 1);

            PlayerAccount again = book.Join("p1", "阿明改名");
            Check("同一個 pid 再次加入不會重複建立", book.PlayerCount == 1);
            Check("重新加入保留籌碼", again.Balance == race.StartingChips);
            Check("重新加入會更新暱稱", again.Nickname == "阿明改名");

            // 暱稱來自現場手機，是不可信輸入
            PlayerAccount nameless = book.Join("p2", "");
            Check("空暱稱會給預設名字", !string.IsNullOrEmpty(nameless.Nickname));
            PlayerAccount messy = book.Join("p3", "壞名 字非常非常非常長超過上限");
            Check("暱稱會去掉控制字元並截斷長度",
                messy.Nickname.IndexOf('') < 0
                && messy.Nickname.IndexOf(' ') < 0
                && messy.Nickname.Length <= BettingBook.MaxNicknameLength);

            // --- 下注 ---
            book.BeginRace();
            int before = ming.Balance;

            Check("正常下注會成功",
                book.TryPlaceBet("p1", 0, 100, 4) == BetRejection.None);
            Check("下注當下就扣籌碼", ming.Balance == before - 100);
            Check("注單記錄正確", ming.StakeOn(0) == 100 && ming.TotalStaked == 100);

            book.TryPlaceBet("p1", 0, 50, 4);
            Check("押同一匹會併成一筆",
                ming.Bets.Count == 1 && ming.StakeOn(0) == 150);

            book.TryPlaceBet("p1", 2, 200, 4);
            Check("押不同匹會分開記",
                ming.Bets.Count == 2 && ming.StakeOn(2) == 200 && ming.TotalStaked == 350);

            Check("找不到的玩家會被拒絕",
                book.TryPlaceBet("nobody", 0, 100, 4) == BetRejection.UnknownPlayer);
            Check("無效閘號會被拒絕",
                book.TryPlaceBet("p1", 9, 100, 4) == BetRejection.InvalidLane);
            Check("低於最低下注額會被拒絕",
                book.TryPlaceBet("p1", 0, race.MinimumBet - 1, 4) == BetRejection.BelowMinimum);
            Check("籌碼不足會被拒絕",
                book.TryPlaceBet("p1", 0, ming.Balance + 1, 4) == BetRejection.InsufficientChips);

            int balanceAfterRejections = ming.Balance;
            book.TryPlaceBet("p1", 0, ming.Balance + 1, 4);
            Check("被拒絕的下注不會動到籌碼", ming.Balance == balanceAfterRejections);

            // --- 結算 ---
            double[] odds = { 3.0, 5.0, 2.0, 8.0 };
            int stakedTotal = ming.TotalStaked;      // 150 押 0 號、200 押 2 號
            int balanceBeforeSettle = ming.Balance;

            SettlementResult settlement = book.Settle(0, odds);

            Check("押中的玩家拿到 注額 × 賠率",
                ming.Balance == balanceBeforeSettle + (int)Math.Round(150 * 3.0));
            Check("派彩明細含中獎玩家",
                settlement.PayoutByPlayer.ContainsKey("p1")
                && settlement.PayoutByPlayer["p1"] == 450);
            Check("淨輸贏計算正確（派彩 450 − 押注 350 = +100）",
                ming.LastDelta == 450 - stakedTotal);
            Check("冠軍閘號有記錄", settlement.WinnerLane == 0);

            PlayerAccount loser = book.Find("p2");
            book.BeginRace();
            book.TryPlaceBet("p2", 1, 100, 4);
            int loserBalance = loser.Balance;
            book.Settle(0, odds);
            Check("沒押中的玩家不會再被扣錢（本金下注時已扣）",
                loser.Balance == loserBalance);
            Check("沒押中的玩家淨輸贏為負", loser.LastDelta == -100);
            Check("沒押中的玩家不出現在派彩明細",
                !book.Settle(0, odds).PayoutByPlayer.ContainsKey("p2"));

            // 完全沒下注的玩家不該被影響
            PlayerAccount idle = book.Join("p9", "旁觀者");
            int idleBalance = idle.Balance;
            book.BeginRace();
            book.Settle(0, odds);
            Check("沒下注的玩家籌碼不變", idle.Balance == idleBalance);
            Check("沒下注的玩家淨輸贏為 0", idle.LastDelta == 0);

            // --- 換場清空與同情籌碼 ---
            book.BeginRace();
            Check("換場會清空注單", ming.Bets.Count == 0);

            PlayerAccount broke = book.Join("p4", "輸光");
            broke.Balance = 0;
            book.BeginRace();
            Check("輸光的玩家會被補到同情籌碼", broke.Balance == race.CharityChips);

            PlayerAccount rich = book.Join("p5", "有錢");
            rich.Balance = race.StartingChips * 5;
            book.BeginRace();
            Check("籌碼足夠的玩家不會被同情籌碼影響",
                rich.Balance == race.StartingChips * 5);

            RaceConfig noCharity = DefaultConfig().Race;
            noCharity.CharityChips = 0;
            noCharity.Validate();
            BettingBook strict = new BettingBook(noCharity);
            PlayerAccount bankrupt = strict.Join("x", "破產");
            bankrupt.Balance = 0;
            strict.BeginRace();
            Check("同情籌碼設 0 時不補錢", bankrupt.Balance == 0);

            // --- 排行榜 ---
            BettingBook ranking = new BettingBook(race);
            ranking.Join("a", "A").Balance = 500;
            ranking.Join("b", "B").Balance = 1500;
            ranking.Join("c", "C").Balance = 1000;
            List<PlayerAccount> top = ranking.TopPlayers(2);
            Check("排行榜依籌碼由高到低",
                top.Count == 2 && top[0].PlayerId == "b" && top[1].PlayerId == "c");
            Check("排行榜取全部時不會少人", ranking.TopPlayers(0).Count == 3);

            // --- 流程層的階段檢查 ---
            GameConfig loopConfig = DefaultConfig();
            loopConfig.Race.WaitForHostToStart = false; // 這裡只測下注的階段檢查，跳過開賽前的等待入場
            loopConfig.Race.IdleSeconds = 1.0;
            loopConfig.Race.BettingSeconds = 2.0;
            GameLoop loop = new GameLoop(loopConfig, 7);
            loop.Book.Join("p1", "阿明");

            Check("待機階段不能下注",
                loop.TryPlaceBet("p1", 0, 100) == BetRejection.NotBettingPhase);

            loop.SkipPhase(); // Idle -> Betting
            Check("下注階段可以下注",
                loop.TryPlaceBet("p1", 0, 100) == BetRejection.None);

            loop.SkipPhase(); // Betting -> Racing
            Check("比賽開始後不能再下注",
                loop.TryPlaceBet("p1", 0, 100) == BetRejection.NotBettingPhase);

            loop.SetOdds(loop.RaceNumber, new[] { 2.0, 3.0, 4.0, 5.0 });
            loop.SkipPhase(); // Racing -> Photo
            loop.SkipPhase(); // Photo -> Settle
            Check("進入結算階段時自動派彩", loop.LastSettlement != null);
            Check("結算的冠軍與名次一致",
                loop.LastSettlement.WinnerLane == loop.FinishOrder[0]);
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

            NetworkConfig defaultNetwork = new NetworkConfig();
            defaultNetwork.Validate();
            Check("預設會自動開本機伺服器與外網通道",
                defaultNetwork.AutoStartLocalRelay && defaultNetwork.UseTunnel);

            NetworkConfig brokenNetwork = new NetworkConfig
            {
                NodeCommand = "",
                CloudflaredDownloadUrl = null,
                ServerDirectory = null,
                CloudflaredPath = null,
                TunnelTimeoutSeconds = -1.0
            };
            brokenNetwork.Validate();
            Check("空的 Node 指令退回預設", brokenNetwork.NodeCommand == NetworkConfig.DefaultNodeCommand);
            Check("空的下載網址退回預設",
                brokenNetwork.CloudflaredDownloadUrl == NetworkConfig.DefaultCloudflaredDownloadUrl);
            Check("null 路徑修正為空字串（代表自動尋找）",
                brokenNetwork.ServerDirectory == "" && brokenNetwork.CloudflaredPath == "");
            Check("通道逾時被夾到下限", brokenNetwork.TunnelTimeoutSeconds >= 5.0);

            NetworkConfig slowNetwork = new NetworkConfig { TunnelTimeoutSeconds = 99999.0 };
            slowNetwork.Validate();
            Check("通道逾時被夾到上限", slowNetwork.TunnelTimeoutSeconds <= 300.0);

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
            config.Race.WaitForHostToStart = false; // 等待入場另有專門測試（LobbyTests）
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

        // ---------------------------------------------------------------- 道具券

        /// <summary>建立一個已經進入 Racing 的 GameLoop（跳過等待入場、Idle、Betting）。</summary>
        private static GameLoop RacingLoop(GameConfig config, int seed)
        {
            config.Race.WaitForHostToStart = false;
            GameLoop loop = new GameLoop(config, seed);
            loop.SkipPhase(); // Idle -> Betting
            loop.SkipPhase(); // Betting -> Racing
            return loop;
        }

        private static void ItemShopTests()
        {
            Section("道具券（ItemShop）");

            GameConfig config = DefaultConfig();
            ItemConfig items = config.Items;
            Check("預設券價為 5 籌碼", items.Cost == 5);
            Check("預設不限張數", items.UsesPerRace == 0);
            Check("冷卻設 0 時等於效果時間（效果結束就能再買）",
                Math.Abs(items.EffectiveCooldownSeconds - items.DurationSeconds) < 1e-12);
            ItemConfig explicitCooldown = new ItemConfig { CooldownSeconds = 5.0 };
            Check("有設定冷卻時以設定為準", Math.Abs(explicitCooldown.EffectiveCooldownSeconds - 5.0) < 1e-12);

            // --- 階段檢查 ---
            GameConfig bettingConfig = DefaultConfig();
            bettingConfig.Race.WaitForHostToStart = false;
            GameLoop betting = new GameLoop(bettingConfig, 51);
            betting.SkipPhase(); // Idle -> Betting
            betting.Book.Join("p1", "阿明");
            Check("下注階段不能買券",
                betting.TryUseItem("p1", EffectKind.Boost, 0) == ItemRejection.NotRacing
                && betting.Book.Find("p1").Balance == bettingConfig.Race.StartingChips);

            // --- 成功購買 ---
            GameLoop loop = RacingLoop(DefaultConfig(), 52);
            PlayerAccount buyer = loop.Book.Join("p1", "阿明");
            int before = buyer.Balance;

            Check("比賽中買加速券成功", loop.TryUseItem("p1", EffectKind.Boost, 0) == ItemRejection.None);
            Check("扣掉券價", buyer.Balance == before - items.Cost);
            Check("效果已套到那匹馬身上", loop.Race.ActiveEffectCount(0) == 1);
            SpeedEffect applied = loop.Race.Horses[0].Effects[0];
            Check("效果的種類、倍率、來源正確",
                applied.Kind == EffectKind.Boost
                && Math.Abs(applied.Multiplier - items.BoostMultiplier) < 1e-12
                && applied.SourceNickname == "阿明");

            Check("同一種券在效果期間不能再買",
                loop.TryUseItem("p1", EffectKind.Boost, 1) == ItemRejection.CoolingDown);
            Check("冷卻中被拒絕不扣錢", buyer.Balance == before - items.Cost);
            Check("剛買完剩餘冷卻約等於效果時間",
                Math.Abs(loop.ItemCooldownRemaining("p1", EffectKind.Boost) - items.EffectiveCooldownSeconds) < 0.05);

            Check("兩種券各自冷卻：加速冷卻中仍可買減速",
                loop.TryUseItem("p1", EffectKind.Slow, 2) == ItemRejection.None);

            // 分小步推進：引擎每次 Tick 最多追 MaxCatchUpSeconds，一次餵 2 秒只會前進 0.25 秒
            for (double waited = 0.0; waited < items.EffectiveCooldownSeconds + 0.1; waited += 0.05)
            {
                loop.Tick(0.05);
            }

            Check("效果結束後就能再買同一種券",
                loop.ItemCooldownRemaining("p1", EffectKind.Boost) == 0.0
                && loop.TryUseItem("p1", EffectKind.Boost, 0) == ItemRejection.None);

            // --- 各種拒絕 ---
            Check("沒見過的玩家被拒", loop.TryUseItem("ghost", EffectKind.Boost, 0) == ItemRejection.UnknownPlayer);
            loop.Book.Join("p2", "小美");
            Check("無效閘號被拒", loop.TryUseItem("p2", EffectKind.Boost, 99) == ItemRejection.InvalidLane);

            PlayerAccount poor = loop.Book.Join("p3", "阿窮");
            poor.Balance = items.Cost - 1;
            Check("籌碼不足被拒且不扣錢",
                loop.TryUseItem("p3", EffectKind.Slow, 1) == ItemRejection.InsufficientChips
                && poor.Balance == items.Cost - 1);

            GameLoop crowded = RacingLoop(DefaultConfig(), 53);
            for (int i = 0; i < items.MaxStacksPerHorse; i++)
            {
                crowded.Book.Join("c" + i, "玩家" + i);
                crowded.TryUseItem("c" + i, EffectKind.Slow, 0);
            }

            PlayerAccount latecomer = crowded.Book.Join("late", "晚到");
            int latecomerBalance = latecomer.Balance;
            Check("同一匹馬效果疊滿後被拒且不扣錢",
                crowded.TryUseItem("late", EffectKind.Slow, 0) == ItemRejection.HorseEffectsFull
                && latecomer.Balance == latecomerBalance);
            Check("效果疊滿時冷卻不會開始",
                crowded.ItemCooldownRemaining("late", EffectKind.Slow) == 0.0);

            GameConfig limitedConfig = DefaultConfig();
            limitedConfig.Items.UsesPerRace = 1;
            GameLoop limited = RacingLoop(limitedConfig, 54);
            limited.Book.Join("p1", "阿明");
            limited.TryUseItem("p1", EffectKind.Boost, 0);
            Check("設定張數上限時用完就被拒",
                limited.TryUseItem("p1", EffectKind.Slow, 1) == ItemRejection.NoUsesLeft);

            HorseConfig[] lineup = RaceLineup.Create(config.Race, config.Roster, 55);
            RaceEngine finishedRace = new RaceEngine(config.Race, lineup, 55);
            finishedRace.RunToCompletion();
            ItemShop shop = new ItemShop(config.Items);
            Check("已衝線的馬不能再用券",
                shop.TryUse(new PlayerAccount { PlayerId = "x", Balance = 100 }, EffectKind.Boost, 0, finishedRace)
                == ItemRejection.HorseFinished);

            // --- 換場 ---
            GameLoop cycle = RacingLoop(DefaultConfig(), 56);
            cycle.Book.Join("p1", "阿明");
            cycle.TryUseItem("p1", EffectKind.Boost, 0);
            cycle.SkipPhase(); // Racing -> Photo（直接跑完）
            cycle.SkipPhase(); // Photo -> Settle
            cycle.SkipPhase(); // Settle -> Idle
            cycle.SkipPhase(); // Idle -> Betting
            cycle.SkipPhase(); // Betting -> Racing
            Check("新的一場冷卻歸零",
                cycle.Phase == RacePhase.Racing && cycle.ItemCooldownRemaining("p1", EffectKind.Boost) == 0.0
                && cycle.TryUseItem("p1", EffectKind.Boost, 0) == ItemRejection.None);

            // --- 協定字串 ---
            EffectKind parsed;
            Check("協定字串 boost／slow 可解析",
                HorseRace.Core.Protocol.ItemKinds.TryParse("boost", out parsed) && parsed == EffectKind.Boost
                && HorseRace.Core.Protocol.ItemKinds.TryParse("slow", out parsed) && parsed == EffectKind.Slow);
            Check("不認得的字串一律拒絕",
                !HorseRace.Core.Protocol.ItemKinds.TryParse("BOOST", out parsed)
                && !HorseRace.Core.Protocol.ItemKinds.TryParse(null, out parsed)
                && !HorseRace.Core.Protocol.ItemKinds.TryParse("stop", out parsed));
            Check("種類與字串可互轉",
                HorseRace.Core.Protocol.ItemKinds.ToWire(EffectKind.Slow) == "slow"
                && HorseRace.Core.Protocol.ItemKinds.ToWire(EffectKind.Boost) == "boost");
        }

        // ---------------------------------------------------------------- 每人步數上限

        private static void StepGateTests()
        {
            Section("每人步數上限（StepGate）");

            RaceConfig race = new RaceConfig { MaxStepsPerSecondPerPlayer = 10.0 };
            StepGate gate = new StepGate(race);

            Check("一開始最多允許一秒的額度", gate.Admit("a", 25, 0.0) == 10);
            Check("半秒後只補回一半", gate.Admit("a", 10, 0.5) == 5);
            Check("不同玩家各自計算", gate.Admit("b", 8, 0.5) == 8);
            Check("零或負步數不計入", gate.Admit("a", 0, 1.0) == 0 && gate.Admit("a", -4, 1.0) == 0);

            StepGate sustained = new StepGate(race);
            int total = 0;
            for (int tick = 0; tick <= 50; tick++)
            {
                total += sustained.Admit("spam", 3, tick * 0.1); // 每秒要求 30 步
            }

            Check("持續狂點 5 秒，計入量不超過上限（實得 " + total + " 步，上限約 60）",
                total >= 55 && total <= 61);

            StepGate unlimited = new StepGate(new RaceConfig { MaxStepsPerSecondPerPlayer = 0.0 });
            Check("上限設 0 代表不限", unlimited.Admit("a", 999, 0.0) == 999);

            sustained.Reset();
            Check("Reset 後額度重新給滿", sustained.Admit("spam", 50, 0.0) == 10);

            // --- 實際效果：一個人撐不滿，要好幾個人 ---
            GameConfig config = DefaultConfig();
            Check("全力門檻遠高於一個人的上限",
                config.Race.StepsPerSecondForFullDrive >= config.Race.MaxStepsPerSecondPerPlayer * 2.5);

            double solo = DriveLevelWithPlayers(1, 30);
            Check("一個人拼命搖（每秒要求 30 步）也只推到約三分之一（實得 " + solo.ToString("F2") + "）",
                solo > 0.2 && solo < 0.42);

            double trio = DriveLevelWithPlayers(3, 10);
            Check("三個人一起搖（每人每秒 10 步）接近全滿（實得 " + trio.ToString("F2") + "）",
                trio > 0.85);

            GameConfig notRacingConfig = DefaultConfig();
            notRacingConfig.Race.WaitForHostToStart = false;
            GameLoop notRacing = new GameLoop(notRacingConfig, 61);
            Check("不在比賽中時步數不計入", notRacing.AddSteps("a", 0, 5) == 0);
        }

        /// <summary>指定人數一起替 0 號馬搖 5 秒，每人每秒要求指定步數，回傳最後的驅動強度。</summary>
        private static double DriveLevelWithPlayers(int players, int stepsPerSecondEach)
        {
            GameLoop loop = RacingLoop(DefaultConfig(), 60);
            const double dt = 0.1;
            double owed = 0.0;
            for (int tick = 0; tick < 50; tick++)
            {
                owed += stepsPerSecondEach * dt;
                int steps = (int)owed;
                owed -= steps;
                for (int p = 0; p < players; p++)
                {
                    loop.AddSteps("p" + p, 0, steps);
                }

                loop.Tick(dt);
            }

            return loop.Race.DriveLevelOf(0);
        }

        // ---------------------------------------------------------------- 開賽前等待入場

        private static void LobbyTests()
        {
            Section("Lobby（等待入場）");

            GameConfig config = DefaultConfig();
            Check("預設設定會先等主持人開始", config.Race.WaitForHostToStart);

            List<RacePhase> visited = new List<RacePhase>();
            GameLoop loop = new GameLoop(config, 31);
            loop.PhaseEntered += visited.Add;

            Check("起始階段為 Lobby", loop.Phase == RacePhase.Lobby);
            Check("Lobby 沒有倒數", loop.PhaseRemainingSeconds == 0.0);
            Check("Lobby 時名單已備妥（可以先算賠率）", loop.Lineup != null && loop.Lineup.Length == 4);

            loop.Tick(600.0);
            Check("時間再久也停在 Lobby，不會偷偷開始", loop.Phase == RacePhase.Lobby && visited.Count == 0);

            loop.Book.Join("p1", "阿明");
            Check("Lobby 期間可以入場", loop.Book.PlayerCount == 1);
            Check("Lobby 期間不能下注",
                loop.TryPlaceBet("p1", 0, 100) == BetRejection.NotBettingPhase);

            Check("主持人開始後回傳 true", loop.StartFromLobby());
            Check("開始後進入第一場的 Idle",
                loop.Phase == RacePhase.Idle && loop.RaceNumber == 1
                && visited.Count == 1 && visited[0] == RacePhase.Idle);
            Check("Idle 恢復正常倒數", loop.PhaseRemainingSeconds > 0.0);
            Check("已經開始後再按一次不會有效果", !loop.StartFromLobby() && loop.Phase == RacePhase.Idle);

            GameLoop skipper = new GameLoop(config, 32);
            skipper.SkipPhase();
            Check("除錯跳階段在 Lobby 等同開始", skipper.Phase == RacePhase.Idle);

            // 跑完一整場之後不會再回到 Lobby
            config.Race.IdleSeconds = 0.5;
            config.Race.BettingSeconds = 0.5;
            config.Race.PhotoSeconds = 0.5;
            config.Race.SettleSeconds = 0.5;
            GameLoop full = new GameLoop(config, 33);
            full.StartFromLobby();
            double elapsed = 0.0;
            while (full.RaceNumber == 1 && elapsed < 300.0)
            {
                full.Tick(0.05);
                elapsed += 0.05;
            }

            Check("第一場結束後直接進第二場的 Idle，不回 Lobby",
                full.RaceNumber == 2 && full.Phase == RacePhase.Idle);

            GameConfig unattended = DefaultConfig();
            unattended.Race.WaitForHostToStart = false;
            Check("關閉等待時直接從 Idle 開始", new GameLoop(unattended, 34).Phase == RacePhase.Idle);

            PresentationConfig presentation = new PresentationConfig
            {
                IntroVideo = null,
                PlaceholderSeconds = -5.0
            };
            presentation.Validate();
            Check("開場設定：null 影片路徑修正為空字串", presentation.IntroVideo == "");
            Check("開場設定：預設畫面長度夾到下限", presentation.PlaceholderSeconds >= 1.0);

            GameConfig missingSection = new GameConfig { Presentation = null };
            missingSection.Validate();
            Check("設定檔缺少開場區段時補上預設值",
                missingSection.Presentation != null && missingSection.Presentation.PlayIntro);
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
