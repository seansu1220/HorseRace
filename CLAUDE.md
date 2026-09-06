# 專案：多人連線賽馬遊戲（大螢幕 + 手機下注）

現場活動用的互動遊戲。一台電腦跑 Unity 大螢幕程式扮演「賽馬場」，
現場觀眾用手機掃 QRCode 進入網頁，即時下注並在賽中使用道具干預賽況。

- Unity 2022.3.22f1，Built-in Render Pipeline，目標平台 **Windows Standalone (x64)**
- 手機端是**純網頁**（HTML/CSS/JS），不做 App、不上架
- 雲端中繼伺服器 Node.js，負責靜態網頁託管與 WebSocket 訊息轉發
- 規模設定：同場 10～30 名玩家
- 籌碼純娛樂性質、只存在記憶體，**不涉及真實金流**
- 開發者主力語言 C#，JS/Node 部分依賴 AI 協助

---

## 工作流程規範

- **模型分工（省 token，預設執行、不需使用者每次交代）**：主模型只負責「規劃任務規格」與「最終驗收」（讀 diff、核對引用、跑編譯/自測、抓邏輯洞）；**實際程式修改一律用 Agent tool 交給 Opus 5 子代理執行**（subagent_type: general-purpose, model: opus，規格需寫明檔案錨點與專案規範）。例外：改動極小（幾行內）或任務特別複雜高風險（賽事模擬核心、連線協定變更、需要主對話大量上下文的除錯）才由主模型直接改。**若當前主模型本身即為 Opus 5，則不需分工，規劃、實作、驗收全部由 Opus 5 從頭做到尾。**
- **完成修改程式後，務必幫使用者 commit 到 GitHub**（除非使用者明確表示不需要）
- 請用繁體中文與使用者對話
- **每次修改程式後，將本次變更補充至 `docs/CHANGELOG.md`**，格式包含：問題描述、根本原因、修改的檔案與內容；此檔案透過 git 同步，供多台電腦查閱歷史紀錄

### .gitignore（Unity 專案專用，務必確認）

```
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
*.csproj
*.sln
*.user
*.unityproj
.vs/
.idea/
*.pidb
*.booproj
sysinfo.txt
.env
node_modules/
```

**`Library/` 一定要排除**——Unity 會在裡面塞數 GB 的快取，誤入版控會讓 repo 直接爆掉。
`server/node_modules/` 同理。建置輸出不進 git，部署時另行處理。

### 依賴管理

- **Unity 端**：透過 Package Manager 管理，變更反映在 `Packages/manifest.json`，該檔案要進版控。
  外部 DLL 一律放 `Assets/Plugins/`，並在 `docs/THIRD_PARTY_NOTICES.md` 記錄授權。
  只採用 **MIT / Apache-2.0 / CC0** 授權，避免 GPL 與 CC BY-SA（share-alike 對商業活動有風險）。
- **Node 端**：`server/package.json`，相依套件盡量壓到最少（目前只需要 `ws` 與 `express`）。
- 需要下載的依賴要寫進對應的依賴檔，並在第一次執行時自動檢查安裝。

---

## 🏗 系統架構（三方角色）

```
                  ☁️  雲端中繼伺服器 (Node.js)
                  ├─ 靜態網頁託管：手機下注頁
                  ├─ WS /host   ← Unity 大螢幕（每房間僅一個）
                  └─ WS /play   ← 手機玩家（多個）
                          │
          ┌───────────────┴────────────────┐
   Unity Windows 大螢幕                手機瀏覽器 × N
   （賽事模擬的唯一權威）          （掃 QR → https://.../r/<房號>）
```

### 權威性原則（重要）

**Unity 主機是唯一權威**：賽事模擬、賠率、籌碼帳本、道具判定全部在 C# 端運算。
**雲端伺服器只是啞管線**——負責房間路由、訊息轉發、靜態檔託管、斷線重連的狀態回放，
**不得寫入任何遊戲邏輯**。這條界線讓遊戲規則全部留在可測試的純 C#，
換伺服器、換部署商都不影響玩法。

伺服器持有的唯一狀態是「Unity 推來的最新快照」，用於玩家重連時立即補上畫面。

### 訊息流向

| 方向 | 訊息 | 頻率 |
|---|---|---|
| Host → 全體 | `phase` 階段變更（含倒數截止時間戳） | 事件觸發 |
| Host → 全體 | `snapshot` 賽況快照（進度、名次） | 賽中 10 Hz |
| Host → 單人 | `wallet` 個人籌碼／注單／道具冷卻 | 事件觸發 |
| Host → 全體 | `result` 名次與派彩 | 事件觸發 |
| Player → Host | `join` / `bet` / `item` | 玩家操作 |

**倒數計時一律傳「結束時間戳」而非「剩餘秒數」**，由手機端自行遞減，避免網路抖動造成秒數跳動。

---

## 📁 目錄結構

```
HorseRace/
├─ Assets/
│  ├─ Scripts/
│  │  ├─ Core/          純 C#，禁止 using UnityEngine
│  │  ├─ Net/           WebSocket 客戶端、重連、訊息序列化
│  │  ├─ View/          Unity 場景、馬匹、攝影機、UI、QRCode
│  │  └─ Config/        ScriptableObject 設定資產
│  ├─ Resources/        執行期載入的素材（字型、材質、模型）
│  └─ Plugins/          第三方 DLL
├─ server/              Node.js 中繼伺服器
│  ├─ src/
│  └─ public/           手機端網頁（HTML/CSS/JS，純靜態）
├─ tools/CoreTests/     dotnet console 專案，不開 Unity 也能測 Core
└─ docs/
   ├─ ARCHITECTURE.md   架構細節與訊息協定
   ├─ CHANGELOG.md      變更紀錄
   └─ THIRD_PARTY_NOTICES.md
```

`tools/` 與 `server/` 都在 `Assets/` 之外，Unity 不會編譯到。

---

## 📐 核心架構原則

> 適用所有專案的通用設計準則。新功能請遵照執行；既有程式碼在不破壞功能的前提下逐步靠攏。

### 1. 職責分離 (Separation of Concerns)
UI 層只負責顯示與事件捕捉，不寫商業邏輯。核心邏輯模組不依賴特定框架或介面，確保未來可獨立測試或替換前端。

**本專案的具體落實**：`Assets/Scripts/Core/` 底下**禁止 `using UnityEngine`**。
賽事模擬、賠率、下注結算之後可能要搬到伺服器端做權威判定，必須是純 C#。
- 隨機數用 `Core/DeterministicRandom`（SplitMix64），不用 `UnityEngine.Random`，
  也不用 `System.Random`——後者無法複製內部狀態，`RaceEngine.Clone()` 會失去可重現性
- 不使用 `Debug.Log`，需要輸出改用回傳值或事件
- 不使用 `MonoBehaviour`、`Coroutine`、`Vector3`、`Time.deltaTime` 等 Unity 型別
- 時間一律由外部以 `Tick(double deltaSeconds)` 注入，Core 自己不讀時鐘

### 2. 型別先行 (Type-First)
新增跨模組傳遞的資料結構前，先用 C# 的 class / struct / enum 明確定義，再寫邏輯。函式簽名標注輸入輸出型別。

**本專案的具體落實**：階段、道具種類、下注類型一律用 enum，不用 magic string。
所有網路訊息在 `Core/Protocol/` 定義 DTO 類別，**手機端 JS 的欄位名必須與 DTO 完全一致**，
協定變更時兩邊要同一個 commit 一起改。

### 3. 配置驅動，避免魔術數字 (Data-Driven)
業務參數、規則數值不寫死在邏輯中，集中放在設定檔或透過參數注入。調整規則只改設定，不動核心程式碼。

**本專案的具體落實**：賽道長度、下注倒數秒數、初始籌碼、道具效果強度與冷卻、抽水率、
馬匹屬性，全部集中在 `RaceConfig` / `HorseConfig`，並以 ScriptableObject 暴露給 Inspector 調整。
**程式碼中不得出現任何平衡性數值**。現場調參數要能不重編譯就生效。

### 4. 最小化可變共享狀態 (Minimize Shared Mutable State)
計算類輔助函式盡量寫成純函式（輸入 → 輸出，無副作用）。多執行緒或非同步場景下，避免直接讀寫共用變數，優先用訊息傳遞（callback、queue）溝通。

**本專案的具體落實**：WebSocket 訊息在背景執行緒抵達，
**一律丟進 thread-safe queue，由主執行緒在 `Update()` 統一消化**，
不得在網路回呼中直接改動 `RaceEngine` 或觸碰任何 Unity API。
賠率的蒙地卡羅模擬必須用 `RaceEngine` 的複本，不得改動真實賽況。

---

## 🛠 程式碼品質要求

- **異常處理：** 網路連線、JSON 反序列化、檔案 I/O 必須有錯誤捕捉，錯誤訊息需說明發生位置與原因，不可靜默崩潰。**現場活動中程式絕對不能整個掛掉**——單一玩家的異常封包只能踢掉那個玩家，不能影響賽事。
- **命名語意化：** 變數與函式名稱清楚表達意圖，避免 `data`、`tmp`、`x` 等無意義命名。
- **函式單一職責：** 單一函式以 50 行為參考上限，過長時優先用子函式拆解職責。
- **測試同步：** 每新增一個 Core 類別，同時在 `tools/CoreTests` 加對應測試。賽事模擬與結算邏輯的改動一律以測試全綠為驗收標準。

### Core 測試

改動 `Assets/Scripts/Core/` 後，**不需開 Unity** 即可驗證：

```
cd tools/CoreTests && dotnet run
```

全綠才算通過。

---

## 本專案專屬硬性規則

1. **賽事模擬必須可重現**
   `RaceEngine` 以固定 seed + 固定 tick 步長推進，同樣的 seed 與同樣的道具輸入序列
   必須跑出完全相同的結果。這是賠率模擬、賽後重播與除錯的基礎。
   **不得使用可變步長的 `Time.deltaTime` 直接餵進模擬**，Core 內部走固定 50 Hz 累加器，
   畫面再對模擬結果做插值。

2. **場景結構用程式生成，素材用設定檔綁定**
   賽道、馬匹、攝影機、UI 由 `Bootstrap` 在執行期生成，
   **不要要求使用者到 Unity 編輯器拖拉引用**。
   但素材（模型、貼圖、音效）透過 `Assets/Resources/` 路徑或 ScriptableObject 欄位指定，
   使用者換素材時只改設定資產，不動程式碼。

3. **素材策略**
   使用者會陸續提供正式素材。**尚未提供的一律先用免費／自製內容頂替**，
   並在 `docs/THIRD_PARTY_NOTICES.md` 記錄來源與授權：
   - 馬匹：Unity 內建 Capsule + Cube 組合，材質用 `Shader.Find("Standard")` 執行期生成，四色區分
   - 賽道：Plane + 程式繪製的分道線貼圖
   - 字型：Noto Sans TC 子集化（SIL OFL）
   - 之後若要找免費素材，優先 **Kenney.nl（CC0）**，其次 Unity Asset Store 免費區
   替換素材時**不得改動 Core 或 Net**，只能動 `View/` 與 `Config/`。

4. **手機網頁的相容性底線**
   目標是 iOS Safari 與 Android Chrome 的近三年版本。
   - 不用需要編譯的前端框架，純原生 JS + 單一 HTML
   - 不用 WebGL/Canvas 動畫，介面以 DOM 為主，省電且穩定
   - 按鈕要夠大（最小 44×44 pt），單手可操作
   - 必須處理「息屏後回來」：`visibilitychange` 時重連並向伺服器索取完整狀態
   - `playerId` 存 `localStorage`，重整或斷線回來要能回到原本身分與籌碼

5. **雲端伺服器的紀律**
   - 伺服器**不得包含遊戲規則**，只做房間路由、轉發、靜態託管、快照快取
   - 部署平台必須支援 **WebSocket 長連線**（Firebase Hosting / Vercel Serverless 不行）
   - 免費層通常會在閒置後休眠，**活動當天務必提前 10 分鐘喚醒並確認連線**
   - 房號短、好念、不易混淆（避開 0/O、1/I/L），有效期只在該場活動

6. **範圍控制（重要）**
   本階段目標是**現場活動可用的完整原型**。
   **不做**：帳號系統、資料庫、真實金流、跨場次排行榜保存、多房間同時運作、
   賽馬育成／養成系統、精緻美術與過場動畫。
   若使用者要求超出此範圍的功能，先提醒這會顯著拉長工期，確認後再動手。

---

## 遊戲流程備忘

```
Idle  待機／展示上場結果
  ↓
Betting  下注階段（預設 30 秒倒數，可設定）
  ↓
Racing  賽事進行，可使用道具
  ↓
Photo  衝線特寫與名次揭曉
  ↓
Settle  派彩結算（預設 8 秒）
  ↓  回到 Idle
```

- 每場 4 匹馬（數量可設定）
- 賠率在下注階段開始前，用蒙地卡羅跑 N 次模擬算出各馬勝率，取倒數乘上 (1 − 抽水率)
- 道具只能在 Racing 階段使用，有次數上限、冷卻時間，並消耗籌碼
- 玩家中途加入：可以入場拿初始籌碼，但只能等下一場才能下注

---

## 目前進度

**M0 專案骨架**
- [x] Unity 2022.3.22f1 專案骨架
- [x] CLAUDE.md 與 `docs/ARCHITECTURE.md`

**M1 賽事引擎**
- [x] Core：`DeterministicRandom` / `RaceConfig` / `HorseConfig` / `ItemConfig` / `GameConfig`
- [x] Core：`RaceEngine` / `OddsCalculator` / `RaceLineup` / `GameLoop`
- [x] `tools/CoreTests` 單元測試（67 項全綠）

**M2 大螢幕視覺**
- [x] View：賽道、馬匹、攝影機運鏡、大螢幕 UI，全部程式生成
- [x] `StreamingAssets/config/race.json` 現場可調參數
- [x] 建置前自動補齊 Always Included Shaders

**尚未開始**
- [ ] M3 server：Node.js 中繼站 + 部署；web：手機下注頁；Net：WebSocket 客戶端與重連；QRCode
- [ ] M4 下注與賠率結算（`BettingBook` / `RoomState`）
- [ ] M5 道具系統接上手機端（引擎側的 `ApplyEffect` 已完成）
- [ ] M6 音效、播報、正式素材替換、30 連線壓測
