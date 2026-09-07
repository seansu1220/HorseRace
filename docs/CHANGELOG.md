# 變更紀錄

## 2026-09-07 — M3／M4 手機連線互動：掃碼入場、下注、派彩

### 需求

先把軟體端的手機互動做完整（實體硬體暫緩）：掃 QRCode 入場、下注、籌碼結算。

### 修改的檔案與內容

**Core**
- `PlayerAccount.cs`：玩家帳戶與注單。純娛樂籌碼、只存記憶體，不落地也不涉及金流。
- `BettingBook.cs`：籌碼帳本。下注時**立即扣款**，所以餘額永遠等於「還能押的錢」，
  不會出現同一筆錢押兩匹的競態。押同一匹會併成一筆，避免手機端注單愈滾愈長。
  拒絕原因用 `BetRejection` enum 回傳，措辭留給呈現層決定。
- `RaceConfig`：新增 `MinimumBet` 與 `CharityChips`。
  **同情籌碼是刻意的設計**——三十人的聚會裡若有人第三場就輸光、之後只能乾坐著，
  場子就冷掉一角，所以每場開賽前把低於門檻的人補到門檻。設 0 可關閉。
- `GameLoop`：擁有帳本；進入 Betting 時清注單並補同情籌碼，進入 Settle 時自動派彩。
  階段檢查放在 `GameLoop.TryPlaceBet` 而非帳本裡，因為「什麼時候可以下注」是流程規則。
- `Protocol/Messages.cs`：新增 `bet` 上行與 `wallet`／`result` 下行。
  `wallet` 帶 `to` 欄位供中繼站定向轉發——沒必要把每個人的籌碼廣播給全場。

**server**
- 新增定向轉發：大螢幕送出的訊息若帶 `to`，只送給該玩家。
  伺服器從 `join` 訊息記住 pid↔連線的對應，這是它唯一「看內容」的地方，
  純粹為了定址，仍然不含任何遊戲規則。
- 手機頁大改：進場（暱稱）→ 下注 → 出力 → 結果 四個畫面依階段自動切換。
  下注採「先選籌碼金額、再點馬」的方式，像賭場的籌碼盤，少一次操作。
  比賽階段預設推**自己押最多的那匹**，沒下注才讓玩家自己挑。

**View**
- `QrCodeBuilder.cs`：用 ZXing.Net 產生 QRCode 貼圖。
- `Net/LocalAddress.cs`：找出本機區網 IP 並推導手機入場網址。
  挑選條件是「有設定閘道」——開發機常有 VMware／VirtualBox／WSL 的虛擬網卡，
  同樣是 Up 也有 IPv4，但沒有預設閘道，手機連不到。
- `RaceHud`：QRCode 面板（白色靜區、網址、人數、連線狀態）與籌碼排行榜。
- `RaceDirector`：處理下注、定向回傳錢包、結算後廣播賽果與排行榜。

**依賴**
- 新增 `Assets/Plugins/ZXing/zxing.dll`（ZXing.Net 0.16.9，Apache-2.0，netstandard2.0）
- 新增 `docs/THIRD_PARTY_NOTICES.md`

### 驗證

- Core 單元測試 **117 項全綠**（新增 37 項下注與派彩測試：扣款時機、注單合併、
  四種拒絕條件皆不扣錢、押中與沒押中的結算、同情籌碼、排行榜、階段檢查）
- 下注流程端對端測試 **36 項全綠**：三支模擬手機接上真的 Unity 播放器與中繼站，
  驗證錢包只送本人不廣播、下注扣款、拒絕條件、以及**真正走到派彩路徑**
  （押遍四匹保證中獎：押中賠率 3.76 拿到 +1380；押遍四匹小虧 12，抽水 15% 生效）
- **QRCode 可掃性驗證**：擷取 1280×720 全畫面後用 jsQR 解碼，
  等同手機從遠處對螢幕掃的情形。解出內容與大螢幕宣告的網址一致。

### 過程中修正的問題

1. `PixelData` 找不到——ZXing 的實際位置是 `ZXing.Rendering.PixelData`，不是 `ZXing.Common`。
   用反射列出 DLL 型別確認後修正。
2. 像素轉換原本硬寫 BGRA 通道順序。改成**取任一通道判斷亮度**：
   QRCode 只有黑白兩色，三個通道值必定相同，如此完全不必賭 ZXing 給的是哪種順序。
3. Unity batch mode 在新增 .cs 檔案的第一次建置會出現
   「型別找不到」——編譯早於資產匯入完成。第二次建置即正常，非程式問題。
4. 殘留的 Unity 行程會讓後續建置以
   「Multiple Unity instances cannot open the same project」失敗，需先清除。
## 2026-09-07 — 體力驅動（計步器／手機搖動）原型

### 需求

使用者希望替每匹馬做一個實體裝置（類似計步器），數字越高馬跑越快。
確認的玩法是「即時驅動 + 上限值」，賠率仍依馬匹自身數據計算、不要求精準。

### 設計決策

1. **體力加成疊在模擬之上，而非取代模擬。**
   使用者選的是「即時驅動」，但同時要求賠率仍用馬匹數據算，這表示模擬必須繼續跑。
   若做成純體力驅動，沒人搖的馬完全不動，一支裝置沒電就毀掉整場。
   因此模擬是基準速度，搖動在上面疊一個受 `MaxDriveBonus` 約束的加成。
2. **體力占比收斂成單一旋鈕。** `RaceConfig.MaxDriveBonus` 調 0.15 是調味、
   調 1.5 接近純體力賽。現場覺得不夠刺激改一個數字就好，不必改程式。
3. **驅動強度用漏水桶而非累計計數。** 每一步往桶裡加水，桶子同時以固定時間常數漏水，
   水位反映「最近的步頻」。好處是封包不必等間隔抵達也不會抖動、停下來會自己降回去、
   裝置斷線時那匹馬平順退回基準速度而不是卡在半路。
4. **先用手機當計步器做原型。** 訊息管線與遊戲邏輯和實體裝置完全相同，
   之後只換掉步數來源。先把「搖幾下等於快多少」調到好玩，再花錢買硬體。

### 修改的檔案與內容

**Core**
- `RaceConfig`：新增 `MaxDriveBonus`、`StepsPerSecondForFullDrive`、`DriveDecaySeconds`
- `HorseState`：新增 `DriveLevel`
- `RaceEngine`：新增 `AddSteps()` / `DriveLevelOf()` 與 `DecayDrive()`，
  目標速度乘上 `1 + MaxDriveBonus * DriveLevel`
- `NetworkConfig`：中繼伺服器位址等設定，開發指向 localhost、活動指向雲端只改 JSON
- `Protocol/Messages.cs`：上下行訊息 DTO。上行刻意用單一扁平結構，
  因為 JsonUtility 沒有多型反序列化能力

**server/（新增）**
- `src/server.js`：Node.js 中繼站。只認 `t` 欄位做路由，不含任何遊戲邏輯。
  快取最後一則階段訊息供新加入者立即補畫面；壞封包只影響送出它的那條連線
- `public/`：手機頁。**同時支援搖動與連打按鈕**，兩者餵同一個計數器
- `README.md`：啟動方式與 HTTPS 說明

**Net/（新增）**
- `RelayClient.cs`：用 .NET 內建 `ClientWebSocket`，不引入任何第三方套件。
  收送都在背景 Task，對外只透過 thread-safe 佇列，主執行緒才碰賽事狀態

**View**
- `RaceDirector`：消化上行訊息、廣播階段與驅動強度、光環改為「道具 > 體力」優先序
- `RaceHud`：每匹馬多一條體力條，右側面板顯示連線狀態

### 踩到的坑（已在程式與文件中處理）

**iOS Safari 與 Android Chrome 只在 HTTPS 下開放 `DeviceMotion`。**
用區網 IP 直接開網頁一定拿不到權限，iOS 連詢問都不會跳。
處理方式有兩層：文件說明用 `cloudflared tunnel --url http://localhost:8080`
取得公開 https 網址；同時手機頁保留「連打」按鈕走同一個計數器，
沒有 HTTPS 或權限被拒時仍可完整測試。

### 驗證狀態

- Core 單元測試 80 項全綠（新增 12 項體力驅動測試，含
  「以全力步頻搖動收斂到 1」「停止後衰減回 0」「狂甩不破上限」
  「持續搖動讓平均名次明顯變好 1.00 < 2.63」「上限設 0 時搖動完全無效」）
- 中繼伺服器端對端測試 15 項全綠（雙向轉發、快取回放、壞封包容錯、
  大螢幕重連接手、目錄穿越防護）
- Unity 編譯零錯誤零警告
- **端對端整合測試 14 項全綠**：同時啟動中繼站與 Unity 播放器，接上模擬手機
  對 0 號馬全力送步數，驗證大螢幕連得上、手機收得到名單與賠率、
  驅動強度衝到 0.98 而其他馬維持 0.000、停止後衰減回 0.022、
  被搖的馬以 16.31 秒對 25.15 秒獲勝、全程無執行期例外。

### 驗證中發現並修正的問題

1. **馬群被擠出畫面**：體力加成讓被搖的馬大幅甩開其他馬，固定鏡位下後段班整個出鏡，
   看起來就不像在比賽。改為對準頭尾中點並依馬群拉開幅度自動拉遠
   （`RaceCameraRig.FollowPack` 改收領先者與最後一名，不再收平均值）。
   退遠時只微幅升高——升太多會把俯角拉大，下半個畫面變成整片空草地。
2. 新增賠率計算耗時的 log。實測 Mono 上 1500 次模擬約 400 ms，
   遠低於 `IdleSeconds`，下注階段開始前一定算得完。這個數字若逼近待機秒數，
   就要調長待機時間或調低模擬次數。
3. 新增賽果 log。賽後有人質疑名次時，這是唯一的客觀紀錄。

註：整合測試中「手機收得到賠率」一度失敗，追查後是測試本身在收到第一則訊息時
就立刻檢查，而賠率是背景算完後才補播的。產品行為正確，修正的是測試。

## 2026-09-06 — M1 賽事引擎 + M2 賽馬場視覺

### 問題描述

架構定案後，專案裡還沒有任何程式碼。需要先做出「不必連網也能跑完一場比賽」的核心，
確認賽事本身好不好玩，再去接雲端與手機端。

### 根本原因

新功能開發，非修 bug。

### 修改的檔案與內容

**Core（純 C#，`noEngineReferences` 強制隔離 UnityEngine）**

- `Core/HorseRace.Core.asmdef` — 用 `noEngineReferences: true` 讓「Core 不得依賴 Unity」
  變成編譯期強制，而不只是口頭約定。
- `Core/DeterministicRandom.cs` — SplitMix64。**不用 `System.Random` 的原因**：
  它無法匯出內部狀態，而 `RaceEngine.Clone()` 必須連亂數狀態一起複製，
  複本才能跑出與本體相同的後續結果。此處與 CLAUDE.md 原本寫的
  「隨機數用 System.Random」略有出入，已同步修正該條規則的措辭。
- `Core/Config/{ConfigMath, HorseConfig, RaceConfig, ItemConfig, GameConfig}.cs` —
  所有平衡性數值集中於此，每個類別都有 `Validate()` 把欄位夾到合理範圍。
  設定壞掉時一律退回預設值而非拋例外：現場活動不能因為一個手殘的 JSON 就開不起來。
- `Core/{SpeedEffect, HorseState, RaceLineup}.cs` — 執行期狀態與出賽名單產生。
- `Core/RaceEngine.cs` — 固定 50 Hz 步長的賽事模擬。
  目標速度 = 基礎速度 × 耐力係數 × 波動係數 × 道具倍率，實際速度平滑逼近目標值。
  波動採均值回歸過程（OU）而非每步獨立的白雜訊，才能自然做出超車與被追上。
  衝線時用超出終點的距離回推精確時刻，同步衝線也分得出前後。
- `Core/OddsCalculator.cs` — 蒙地卡羅估勝率換算固定賠率。
- `Core/GameLoop.cs` — Idle → Betting → Racing → Photo → Settle 階段狀態機。

**View（Unity）**

- `View/HorseRace.View.asmdef`、`Bootstrap.cs` — 場景中什麼都不用放，
  `RuntimeInitializeOnLoadMethod` 自動生出總控、賽道、馬匹與介面。
- `View/TrackLayout.cs` — 視覺尺度與模擬的「公尺」刻意脫鉤，兩者只透過 0~1 完成度連接，
  改動畫面尺寸不會影響任何賽果。
- `View/{MaterialLibrary, TrackBuilder, HorseView, RaceCameraRig}.cs` —
  賽道、馬匹、運鏡全部用基本幾何體與程式生成材質，零外部素材。
- `View/FontProvider.cs` — 向作業系統取中文字型。**刻意不用 TextMeshPro**：
  TMP 需要先在編輯器匯入 Essentials 並烘焙字型圖集，違反「不要求使用者操作編輯器」的規則。
- `View/{UiFactory, RaceHud}.cs` — 大螢幕介面（場次、階段倒數、賠率、即時名次、名次揭曉、
  馬匹名牌）。右側預留 QRCode 區塊給 M3。
- `View/ConfigLoader.cs` — 從 `StreamingAssets/config/race.json` 讀設定。
  放 StreamingAssets 而非 ScriptableObject，是為了讓現場用記事本改參數就能生效。
- `View/RaceDirector.cs` — 總控。賠率的蒙地卡羅丟到 `Task.Run` 背景執行緒跑
  （傳入設定複本，無共用可變狀態），避免階段切換時卡一幀。
- `Assets/Editor/AlwaysIncludedShaders.cs` — 建置前自動把 Standard 等著色器加入
  Always Included Shaders。所有材質都是執行期 `Shader.Find` 建立的，
  沒有這一步打包出來會整片變洋紅色。

**測試**

- `tools/CoreTests/` — 67 項測試，`cd tools/CoreTests && dotnet run` 全綠。
  涵蓋亂數分布與可複製性、模擬決定性、累加器行為、Clone 獨立性、名次排序、
  賠率邊界與單調性、道具效果的實際影響、設定夾取、階段狀態機。
  `TargetFramework` 停在 `netcoreapp2.1` 是因為開發機目前只有 .NET SDK 2.1，
  Core 也因此寫成 C# 7.3 相容語法；日後裝新 SDK 只要改 csproj 一行。

### 過程中修正的問題

- 測試「效果超過持續時間後自動移除」一開始失敗。原因不是引擎有錯，而是測試單次
  `Advance(2.0)` 被 `MaxCatchUpSeconds = 0.25` 的防卡頓上限截斷。
  該上限是刻意設計（避免卡頓後一次補上百步跳過半場比賽），因此改的是測試：
  新增 `AdvanceBySeconds()` 以小片段推進，也更貼近實際每幀呼叫的情形。

### 驗證方式與過程中發現的缺陷

驗證流程：`dotnet run` 跑 Core 測試 → Unity batch mode 編譯 → 建置 Windows 版 →
以 `-capture <資料夾>` 實際執行並由遊戲自行輸出畫面（`Assets/Scripts/View/DebugCapture.cs`，
用 `ScreenCapture` 只擷取遊戲畫格緩衝區）→ 檢查 log 有無執行期例外。
完整跑完 Idle → Betting → Racing → Photo → Settle 且零例外，才算通過。

實際看畫面後抓到五個編譯期看不出來的缺陷：

1. **賠率崩壞（最嚴重）**：初版賠率是 `1.19 / 20.00 / 16.56 / 3.85`，
   等於「最快的那匹幾乎每場都贏」，下注完全失去意義。
   根因是隨機性只來自 `NoiseScale` 的即時波動，而波動會均值回歸——
   26 秒賽程等於取了幾十次獨立取樣，平均下來幾乎抵銷，
   壓不過馬匹之間 3.5% 的實力差距。
   修法是新增 `RaceConfig.FormSpread` 與 `HorseState.Form`：開賽前替每匹馬抽一次
   整場固定的「當日狀態」倍率，不會被平均掉。修正後賠率變成 `1.62 / 5.00 / 5.99 / 5.20`。
   並在 `CoreTests` 加了一項回歸測試，直接斷言預設名冊的賠率必須落在 1.5～12 倍。
2. **分道線與起跑線完全看不見**：`TrackLayout.GroundY` 誤用 `BedHeight * 0.5`，
   但路面方塊的上緣在 `BedHeight`，所有貼地標線都被埋進路面裡。順帶讓馬蹄陷入地面 0.1。
3. **看台變成擋住上半畫面的黑色大板**：鏡頭是低角度側拍，看台屋頂從下方看就是一整片深色底面。
   移除屋頂、看台後退到 `span/2 + 48` 並改用亮色頂緣帶。
4. **近側欄杆在視覺上穿過馬腿**：欄杆只離跑道 0.4，從側面低角度看，
   欄杆頂緣與最近一匹馬的馬蹄幾乎共線。新增 `TrackLayout.RailOffset = 2.5` 拉開距離。
5. **鏡頭取景偏左下**：跟拍的前瞻量 +7 太大，把馬群推到畫面角落，主體變成空賽道。
   降到 +2，並把領先者權重從 0.7 調到 0.55，避免落後者出鏡。
6. **完賽後左側面板顯示四個「100%」**：改成完賽顯示秒數、未完賽顯示完成度。

### 操作方式

Unity 開啟專案直接按 Play。除錯按鍵：
`空白鍵` 跳過階段、`R` 重新開始並重讀設定檔、`1`／`2` 對隨機一匹馬加速／減速、`Esc` 離開。

建置：Unity 選單 `HorseRace ▸ 建置 Windows 版`，或命令列
`-executeMethod HorseRace.EditorTools.PlayerBuild.BuildFromCommandLine`。

## 2026-09-06 — 專案初始化與架構定案

### 問題描述

HorseRace 只有 Unity 空專案骨架（2022.3.22f1，僅一個 SampleScene），
沒有專案規範文件，也還沒決定「大螢幕 + 手機下注」要用什麼架構實現。

### 根本原因

新專案，尚未進行任何規劃。原先沿用的是 `d:/ClaudeCode/CLAUDE.md` 通用規範，
內容針對 Python 專案（requirements.txt、*.pkl），不適用 Unity。

### 決策

與使用者確認三項前提後定案：

- **連線方式**：雲端中繼伺服器（非區網自架）
- **規模**：同場 10～30 名玩家
- **籌碼**：純娛樂性質，只存記憶體，不做帳號與資料庫

架構採「**Unity 主機為唯一權威、雲端伺服器只做啞管線**」，
遊戲邏輯全部留在純 C# 的 `Assets/Scripts/Core/`，詳見 `docs/ARCHITECTURE.md`。

### 修改的檔案與內容

- **新增 `CLAUDE.md`** — Unity 版專案規範。參考 `Mahjong_MVP/CLAUDE.md` 的結構，
  改寫為賽馬專案：三方角色架構、Unity 版 .gitignore、Core 禁 UnityEngine、
  固定步長可重現模擬、素材策略（未提供者先用免費／程式生成內容）、
  手機網頁相容性底線、雲端伺服器紀律、範圍控制。
- **新增 `docs/ARCHITECTURE.md`** — 架構設計書。含資料流時序、執行緒模型、
  Core 模組劃分、賽事模擬公式、賠率演算法（蒙地卡羅 2000 次取倒數）、
  道具系統設計、WebSocket 訊息協定、連線容錯對照表、技術選型、里程碑 M0～M6、已知風險。
- **新增 `.gitignore`** — Unity 專用，排除 `Library/`、`Temp/`、`node_modules/` 等。
- **新增 `docs/CHANGELOG.md`** — 本檔。

### 設計建議（已記入架構書）

- 「讓馬直接停下來」建議改為「短暫減速 x0.6 持續 2 秒」，
  完全靜止會讓押該匹馬的玩家被單方面剝奪，小場次容易演變成互相報復。
- 賠率採固定賠率而非同注分彩，避免小場次「全押同一匹，賠率剩 1.1」的無趣狀況。
- 部署平台不可用 Firebase Hosting / Vercel Serverless（不支援 WebSocket 長連線）。
