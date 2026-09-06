# 變更紀錄

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
