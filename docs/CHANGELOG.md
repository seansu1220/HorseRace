# 變更紀錄

## 2026-09-26 — 手機頁改版、道具券上線、搖手機變難、拿掉大螢幕舊 QRCode 面板

### 需求

使用者確認設計稿後提出：

1. 手機頁換成設計稿的新外觀
2. 加速券、減速券暫定 5 籌碼；效果結束後就能再買；每張券下方各自選馬，選擇保留、可換；
   買下後券面變全黑不能買，再以 360 度轉回原色，轉完才能再買
3. 下注金額：50、100、500、1000、全部（扣掉已下注後的剩餘），外加自訂輸入框
4. 大螢幕右上角的舊 QRCode 面板拿掉，只留開賽前的大畫面
5. 搖手機／連打要更難：多人會同時搖同一匹，但一個人就能輕鬆搖滿；並詢問全滿時加速多少

### 設計決策

- **券的規則放在 Core（`ItemShop`）**，大螢幕是唯一權威；手機只負責顯示，冷卻倒數以大螢幕回報的剩餘秒數為準。
  兩種券各自冷卻；「冷卻 0 = 等於效果時間」讓「效果結束就能再買」只要一個設定。
- **同一匹馬最多疊 2 層效果保留**：券變成 5 籌碼、不限張數後，30 人同時狂按會讓倍率相乘到接近停止，
  這個上限是防止整場失控的保險。被擋下時不扣錢。
- **搖手機變難的做法是「提高總門檻＋限制個人上限」**，而不是只把門檻調高：
  只調門檻的話，雙手狂點或改過的手機頁還是能一個人撐滿。
  全力門檻 6 → 30 步／秒，每人每秒最多計入 10 步（令牌桶）。實測一個人最多 31%、三個人 90%。
  手機端搖晃門檻也從 3.2 提高到 6.0 m/s²（要真的甩才算）。
- **全滿時的加速**：目標速度 ×(1 + `MaxDriveBonus`) = ×1.6（加快 60%），`race.json` 可調。
- **比賽畫面要顯示場上名次**，所以把賽中推播擴充成含進度與效果旗標（原本只有驅動強度）。

### 修改的檔案與內容

**Core**
- `ItemShop.cs`（新增）：買券規則——階段、玩家、閘號、已衝線、冷卻、張數、效果上限、籌碼依序檢查，
  通過才扣款並套用效果；`ItemRejection` enum 回傳拒絕原因
- `StepGate.cs`（新增）：每名玩家每秒步數上限（令牌桶，容量一秒）
- `GameLoop.cs`：新增 `Items`、`TryUseItem()`、`ItemCooldownRemaining()`、`AddSteps(playerId, …)`；
  進入 Racing 時重置冷卻與步數額度
- `Config/ItemConfig.cs`：`Cost` 50 → 5、`CooldownSeconds` 8 → 0（＝效果時間）、`UsesPerRace` 2 → 0（不限）；
  新增 `EffectiveCooldownSeconds`
- `Config/RaceConfig.cs`：`StepsPerSecondForFullDrive` 6 → 30（上限放寬到 500）、新增 `MaxStepsPerSecondPerPlayer = 10`
- `Protocol/Messages.cs`：新增 `item` 上行與 `ItemKinds`；`step` 帶 `pid`；
  `phase` 加上 `players`、`minBet`、`itemCost`、`itemSeconds`、`itemCooldown`、`boostX`、`slowX`；
  `wallet` 加上 `boostCool`、`slowCool`；`drive` 加上進度 `p` 與效果旗標 `fx`（`EffectFlags`）

**View**
- `RaceDirector.cs`：處理 `item`（拒絕原因翻成玩家看得懂的字）、步數改走 `GameLoop.AddSteps`、
  錢包附冷卻、階段訊息附規則數值與人數、賽況附進度與效果
- `RaceHud.cs`：拿掉右上角的舊 QRCode 面板，改為一行小字（入場人數＋連線狀態）；QRCode 貼圖改由獨立欄位持有

**手機頁（`server/public/`）**：依設計稿整頁改版
- 進場 → 等待（開賽前顯示出賽名單與入場人數；場次之間顯示排行）→ 下注 → 比賽 → 結果
- 下注：50／100／500／1,000／全部 ＋ 自訂輸入框；注單顯示「中可得」金額
- 比賽：場上名次（依進度排序、效果圖示）、兩張券各自的選馬按鈕（選擇保留，預設加速給自己押最多的馬、
  減速給賠率最低的對手）、冷卻動畫（全黑 → 360 度掃回，用 requestAnimationFrame 畫 conic-gradient，
  舊版 iOS Safari 也能顯示）、出力對象與力度條、連打／搖動
- 買券結果顯示在券的下方固定位置，不遮住場上名次
- 數字用 Barlow Condensed（Google Fonts，很小），中文用手機內建字型

**設定與文件**
- `race.json`：新的券價、冷卻、張數與體力門檻
- `docs/ARCHITECTURE.md`：道具規則、體力難度、訊息協定改為實際實作的版本

### 驗證

- Core 單元測試 **176 項全綠**（新增 36 項：券價與預設、階段檢查、扣款、效果套用、各自冷卻、
  冷卻結束可再買、各種拒絕皆不扣錢、效果上限不進冷卻、張數上限、已衝線、換場重置、協定字串、
  令牌桶的爆發與補充、持續狂點不超過上限、**一個人最多 31%、三個人 90%**）
- 用 Unity 自帶 Roslyn 編譯 Core／Net／View／Editor：零錯誤、零 C# 警告
- 手機頁 UI 測試 **27 項全綠**：無頭 Chrome（390×844）開手機頁、模擬大螢幕照規則回應，實際操作並檢查——
  每個階段只顯示一個畫面、金額選項、預設 100 下注、自訂 250 下注、「全部」＝剩餘 650、注單合併與可得金額、
  場上名次依進度排序、兩張券的預設選馬、換馬後保留、買券送出正確種類／馬／玩家、冷卻中不能按且
  另一張不受影響、被拒顯示原因並恢復、2 秒後可再買、連打送出步數帶玩家識別碼、結算輸贏與排行
- 截圖檢查中發現並修正：買券提示原本浮在最上方遮住場上名次（改為券下方固定佔位）；
  冷卻秒數疊在米白券面上看不清（加深色圓底）
- 未能驗證：在 Unity 裡實際跑一場、真的用手機搖（需要使用者操作）

## 2026-09-25 — 修正：手機頁所有畫面同時顯示，看起來沒有分階段

### 問題描述

使用者掃碼進入手機頁後，進場、下注、搖手機、選馬、結果、等待六個畫面全部疊在同一頁，
看起來完全沒有依比賽階段切換。

### 根本原因

`app.js` 用 HTML 的 `hidden` 屬性隱藏非當下階段的畫面，但瀏覽器內建的 `[hidden] { display: none }`
優先權低於任何作者 CSS 規則，`style.css` 的 `.screen { display: flex }` 把它蓋掉了，
於是 `hidden` 完全失效。切換邏輯本身一直是對的，只是看不出來。

這個問題從手機頁第一版就存在。之前的端對端測試只驗證訊息收發，沒有實際渲染畫面，所以沒被發現。

### 修改的檔案與內容

- `server/public/style.css`：新增 `[hidden] { display: none !important; }`，讓 hidden 屬性永遠生效

### 驗證

新增逐階段截圖驗證：以無頭 Chrome（手機尺寸 390×844）開手機頁，同時扮演大螢幕依序送出
lobby／betting／racing／settle 訊息，每個階段截圖並檢查實際可見的畫面：

| 階段 | 修正前可見畫面 | 修正後可見畫面 |
|---|---|---|
| 未入場 | 六個全部 | 進場 |
| 等待開賽 | 六個全部 | 等待（「等待主持人開始」） |
| 下注 | 六個全部 | 下注 |
| 比賽中 | 六個全部 | 搖手機出力 |
| 結算 | 六個全部 | 結果 |

修正後每個階段頁高都剛好一個螢幕（844px），無橫向溢出。

## 2026-09-25 — 修正：外網通道偶發初始化失敗，QRCode 整場卡在「限同 Wi-Fi」

### 問題描述

使用者按 Play 後，大螢幕的 QRCode 顯示「限同 Wi-Fi」的區網網址，外網通道一直沒有出來。
同一份程式在前一次 Play 是正常的。

### 根本原因

Editor.log：`[LocalRelay] 外網通道發生錯誤，QRCode 改用區網網址：TypeInitializationException -
The type initializer for 'HorseRace.Net.QuickTunnel' threw an exception.`

1. `QuickTunnel` 用**靜態欄位**建立查 DNS 用的 `HttpClient`。靜態初始化在背景執行緒第一次用到類別時執行，
   而 Unity 剛進 Play 的瞬間，同時有 WebSocket 連線、埠偵測、賠率計算等多條執行緒在初始化網路元件。
   在這個時機建立 HttpClient 偶爾會失敗；**靜態初始化只要失敗一次，這個類別在整個 Play 期間都無法使用**。
   用 Unity 自帶的 Mono 單獨連跑 30 次無法重現，符合「只在 Unity 剛進 Play 時偶發」的特徵。
2. 通道任務遇到未預期的例外就**直接放棄**，所以一次偶發失敗就讓整場都卡在區網網址。
3. log 只記了最外層例外，真正的原因（InnerException）沒有記到，無法進一步確認。

### 修改的檔案與內容

- `Net/QuickTunnel.cs`：拿掉靜態 HttpClient，改為**用到時才建立的執行個體欄位**，建立失敗只記一次警告、
  改走「20 秒後視為 DNS 已生效」的備援，通道照樣能用；Regex 也改為用到時才建立。
  現在這個類別沒有任何可能失敗的靜態初始化。建構子可注入 HttpClient 工廠（測試用）；實作 `IDisposable`
- `Net/LocalRelayLauncher.cs`：通道遇到未預期錯誤時**不再放棄**，QRCode 暫用區網、10 秒後整個重來；
  通道恢復後 QRCode 自動換回公開網址，不必重開程式
- `Net/ErrorText.cs`（新增）：把例外連同所有內層例外整理成一行（未預期的錯誤另附堆疊）；
  啟動器、通道、Job Object 的錯誤訊息全部改用它

### 驗證

- 啟動器獨立測試新增：
  - 錯誤描述會列出內層例外
  - **注入「建立 HttpClient 一定失敗」**：通道仍在約 27 秒後就緒（20 秒備援＋通道本身），
    警告中記下了內層的真正原因，結束後無殘留 cloudflared
- 完整外網端對端測試 42 項全綠（無退步）
- 用 Unity 自帶 Roslyn 編譯 Core／Net／View／Editor：零錯誤、零 C# 警告
- 若之後在 Unity 再遇到任何通道錯誤，log 會記下完整的內層原因

## 2026-09-25 — 開場動畫與開賽前「等待入場」

### 問題描述

程式一開就直接進入第一場的倒數，但外網通道要十幾秒才就緒：QRCode 還是空白，倒數已經在跑，
觀眾根本來不及入場。使用者也希望之後能在最前面放一段開場動畫。

### 根本原因

流程設計上沒有「開賽前」這個狀態——`GameLoop` 建構時就進入 Idle 並開始計時。

### 設計決策

- **在 Core 新增 `Lobby` 階段而不是只在畫面上擋著。** 「什麼時候開始倒數」是流程規則，必須在
  `GameLoop` 裡、可以測試；手機也要知道現在是開賽前（顯示「等待主持人開始」而不是倒數 0）。
  Lobby 期間時間不流動，只能由 `StartFromLobby()`（主持人按鍵）離開；第一場之後不會再回到 Lobby。
- **主持人按鍵開始**（使用者選定）：現場人什麼時候到齊只有主持人知道。
- **開場只是蓋在最上層的畫布**：伺服器與外網通道照常在背景準備，開場播完時 QRCode 通常已就緒；
  就算還沒好，底下的等待畫面也不倒數，不會吃掉任何時間。
- **換開場影片只要放檔案**：`StreamingAssets/intro/intro.mp4`（路徑可在設定檔改）。
  沒有影片或播放失敗時自動改播程式產生的預設開場，不會卡住。

### 修改的檔案與內容

**Core**
- `GameLoop.cs`：新增 `RacePhase.Lobby`（編號接在最後，既有數值不變）、`StartFromLobby()`；
  Lobby 期間 `Tick` 不推進時間；`SkipPhase` 在 Lobby 等同開始
- `Config/RaceConfig.cs`：新增 `WaitForHostToStart`（預設開；關掉則開程式直接倒數，適合無人值守展示）
- `Config/PresentationConfig.cs`（新增）：`PlayIntro`、`IntroVideo`、`PlaceholderSeconds`、`IntroSkippable`
- `Config/GameConfig.cs`：掛上 `Presentation` 區段，缺少時補預設值
- `Protocol/Messages.cs`：註明 `phase` 可能的值（新增 `lobby`）；Lobby 的 `endsAt` 為 0

**View**
- `IntroPlayer.cs`（新增）：VideoPlayer 播放影片（依影片比例留黑邊、聲音直接輸出），
  準備逾時 8 秒或播放錯誤就改播預設開場（馬匹顏色的色帶奔跑＋標題淡入）；可按空白鍵／Enter／滑鼠略過；
  結束時淡出 0.5 秒後自我銷毀
- `LobbyScreen.cs`（新增）：全螢幕「掃碼入場」——600px 大 QRCode、網址、連線狀態、入場人數、
  最新加入的 10 個名字、主持人提示。人數有變才重組字串
- `RaceHud.cs`：建立並轉交資料給 LobbyScreen（共用同一張 QRCode 貼圖，釋放仍由 RaceHud 負責）；
  Lobby 不顯示倒數
- `RaceDirector.cs`：啟動時先開連線與場景、再疊上開場；開場播放中空白鍵只用來略過開場，
  **不會同時被當成開始比賽**；Lobby 時空白鍵或 Enter 開始第一場；Lobby 廣播 `endsAt = 0`

**手機頁**
- `app.js`／`index.html`：認得 `lobby` 階段，顯示「等待主持人開始」

**設定與文件**
- `race.json`：新增 `Race.WaitForHostToStart` 與 `Presentation` 區段
- `StreamingAssets/intro/README.txt`：開場影片的放置方式與格式建議
- `docs/ARCHITECTURE.md`：時序表加上開場與 Lobby

### 驗證

- Core 單元測試 **140 項全綠**（新增 17 項：起始為 Lobby、Lobby 沒有倒數且時間再久也不會開始、
  Lobby 可入場但不能下注、開始後進入第一場 Idle 並恢復倒數、重複開始無效、除錯跳階段等同開始、
  第一場結束不回 Lobby、關閉等待時直接從 Idle 開始、開場設定的修正與預設值）
- 用 Unity 自帶 Roslyn 編譯 Core／Net／View／Editor：零錯誤、零 C# 警告
- 手機頁 `app.js` 語法檢查通過
- **上一個版本（外網通道）已在使用者的 Unity 編輯器實際驗證**：Editor.log 顯示伺服器自動啟動、
  通道 11.5 秒就緒、QRCode 換成 trycloudflare 網址、手機成功連入並跑完兩場，全程無錯誤；
  停止播放後 node 與 cloudflared 都已收乾淨
- 未能驗證：本次的開場與等待畫面需要在 Unity 按 Play 實際看畫面（編輯器由使用者操作）

## 2026-09-25 — 大螢幕自動啟動中繼伺服器與 Cloudflare 外網通道

### 問題描述

使用者用手機掃大螢幕上的 QRCode 後沒有任何反應。

### 根本原因

1. **中繼伺服器根本沒有開。** QRCode 指向 `http://192.168.1.113:8080/`，但必須另外開終端機執行
   `npm start`，8080 埠上沒有任何程式在聽，手機自然連不上。這個步驟很容易忘，現場也不該依賴它。
2. 就算伺服器有開，區網網址也只有**連同一個 Wi-Fi** 的手機打得開（用行動網路、訪客網路或路由器
   開了 AP 隔離都不行），而且 http 拿不到手機的動作感測器權限。

### 設計決策

- **讓大螢幕自己開伺服器與通道，而不是部署到雲端。** 雲端免費層都會閒置休眠
  （Render 約 15 分鐘，喚醒約 1 分鐘），Railway 已沒有長期免費方案。大螢幕電腦本來就要在現場，
  伺服器跟著它最單純，而且完全免費。
- **用 Cloudflare 臨時通道（Quick Tunnel）取得公開 https 網址**：免帳號、免費，手機用行動網路也能連，
  也拿得到動作感測器權限。代價是網址每次啟動都不同，所以從 cloudflared 的輸出自動抓網址放進 QRCode。
- **通道失敗時自動退回區網網址**：trycloudflare 沒有可用性保證，不能讓它成為單點故障。
  QRCode 下方會標示「限同 Wi-Fi」，背景持續重試，通道好了自動換回。
- **加上大螢幕金鑰**：伺服器對外開放後任何人都連得到，原本只要把連線標成 `role=host`
  就能冒充大螢幕、對全場手機亂發賽果。這不是遊戲規則，只是身分檢查，符合「伺服器只做路由」的紀律。
- **伺服器程式碼不變**：同一份 `server/` 日後仍可部署到雲端，只要把 `RelayUrl` 改成雲端位址，
  本機模式就會自動停用。

### 修改的檔案與內容

**Core**
- `Config/NetworkConfig.cs`：新增 `AutoStartLocalRelay`、`UseTunnel`、`ServerDirectory`、`NodeCommand`、
  `CloudflaredPath`、`CloudflaredDownloadUrl`、`TunnelTimeoutSeconds`，`Validate()` 修正空值與範圍

**Net（新增）**
- `LocalRelayLauncher.cs`：總控。背景啟動 node 伺服器與通道、維護狀態快照、log 佇列；
  該埠已有伺服器時沿用，它關掉後自動接手；伺服器意外結束 3 秒後重開；缺 `node_modules` 自動 `npm install`。
  **完全不碰 Unity API**，主執行緒透過 `Status` 與 `TryDequeueLog` 取資料
- `QuickTunnel.cs`：執行 cloudflared、解析公開網址（排除錯誤訊息裡的 `api.trycloudflare.com`）、
  以「已連上節點」或「實際 HTTP 探測成功」判定就緒；意外結束自動重開，間隔 5→60 秒指數退避
- `CloudflaredInstaller.cs`：依序找設定路徑 → PATH → 先前下載的位置，都沒有就從官方 GitHub 下載。
  先寫 `.part` 再驗證（大小與 `MZ` 檔頭）後改名。**不設總時間上限、改用「60 秒無進度才放棄」**——
  實測這台電腦連 GitHub 只有約 90 KB/s，55 MB 要下載十分鐘以上
- `ChildProcess.cs`：子行程包裝（重導 stdout/stderr、結束通知、收掉行程）
- `ProcessJob.cs`：Windows Job Object（KILL_ON_JOB_CLOSE），大螢幕當掉或被強制結束時子行程一起消失，不留佔著埠的孤兒
- `HostKeyStore.cs`：產生並保存大螢幕金鑰（`persistentDataPath/relay-host-key.txt`）
- `ExecutableLocator.cs`：在 PATH 中找執行檔，找不到時能給出「沒裝 Node.js」這種看得懂的訊息
- `LocalRelayOptions.cs`：把設定與 Unity 路徑轉成純資料；編輯器與建置版的 `server/` 位置用同一條規則推導
- `LocalAddress.cs`：`IsLoopbackHost` 改為公開（並認得 `[::1]`）
- `RelayClient.cs`：被伺服器以 4001／4000 關閉時，狀態顯示「金鑰不符」「已被另一個大螢幕取代」

**View**
- `RaceDirector.cs`：啟動時先開連線再建場景；QRCode 網址優先序為「設定檔指定 → 外網通道 → 區網」，
  通道狀態有變才重算；沒連上時顯示根本原因（例如「找不到 Node.js」）；結束時收掉子行程
- `RaceHud.cs`：沒有網址時真的隱藏 QRCode（原本傳 null 會留著舊圖）；換圖時釋放舊貼圖；
  網址與連線狀態欄位改為放不下就自動縮字
- `UiFactory.cs`：新增 `ShrinkToFit`

**Editor**
- `PlayerBuild.cs`：建置成功後把 `server/`（含 `node_modules`）複製到 exe 旁邊

**server**
- `src/server.js`：新增 `HOST_KEY` 環境變數檢查，金鑰不符以關閉代碼 4001 拒絕（timing-safe 比對）；
  未設定時不檢查，手動 `npm start` 的開發流程不受影響

**設定與文件**
- `StreamingAssets/config/race.json`：新增上述網路欄位
- `docs/ARCHITECTURE.md`：新增 6.1「本機模式」；更新部署平台免費方案現況
- `docs/THIRD_PARTY_NOTICES.md`：新增 cloudflared（Apache-2.0，執行期下載、不隨專案散布）
- `server/README.md`：說明自動啟動與 `HOST_KEY`

### 驗證

- Core 單元測試 **123 項全綠**（新增 6 項 `NetworkConfig` 驗證測試）
- 用 Unity 2022.3.22f1 自帶的 Roslyn 與編輯器產生的 `.rsp` 參考清單編譯 Core／Net／View／Editor 四個組件：
  **零錯誤、零 C# 警告**（編輯器開著專案時無法跑 batch mode，改用此法）
- 啟動器獨立測試程式（Net 層的啟動器完全不依賴 Unity，可直接用 dotnet 編譯執行）共 **50 項全綠**，另加一項強制結束測試：
  - 純函式：網址解析（含排除 `api.trycloudflare.com`）、節點連線辨識、DoH 回應判讀、金鑰附加、
    啟動條件（本機／雲端／關閉／單機）、編輯器與建置版的 `server/` 路徑推導
  - **真實外網端對端**：啟動器自己開伺服器 + cloudflared → 約 16 秒就緒 → 透過
    `https://xxxx.trycloudflare.com` 取得手機頁 → 手機以 `wss://` 經通道連入，
    `join` 到得了大螢幕、定向 `wallet` 只回到該手機 → 關閉後埠釋放、無殘留 cloudflared
  - 金鑰：錯誤金鑰（本機直連與經外網）都被 4001 拒絕，正確金鑰維持連線
  - 沿用外部伺服器 → 外部伺服器關掉 → 啟動器 5 秒內接手，且接手後金鑰檢查生效
  - **強制結束主程式**（模擬當掉）→ node 與 cloudflared 隨之消失、埠釋放（Job Object 生效）
  - 下載：正常下載（55 MB、進度回報到 100%）、下載到錯誤頁或 404 會擋下且不留檔、
    卡住 60 秒準時放棄、慢但持續有進度的下載超過 60 秒仍能完成

### 驗證中發現並修正的問題

1. **cloudflared 回報「已連上節點」時，網址在 DNS 上還查不到。** 第一版以此為就緒訊號，
   測試立刻用本機 DNS 開網址得到「無法識別這台主機」——手機這時掃碼一樣打不開。
   實測網址印出後約 3.5～7 秒公共 DNS 才查得到。更麻煩的是太早查會讓解析器快取「查無此網域」
   （trycloudflare.com 的 SOA 負快取 60 秒），同一個 Wi-Fi 的手機都會跟著失敗。
   改為：已連上節點 **且** 透過 Cloudflare／Google 的 DoH 確認查得到才放出 QRCode；
   刻意不用本機 DNS 查，避免污染現場手機共用的快取；DoH 被擋時 20 秒後視為生效。
2. **下載總時間上限會砍掉慢速但正常的下載。** 這台電腦連 GitHub 只有約 90 KB/s，
   第一版的 10 分鐘總上限不夠。改為「60 秒完全沒有進度才放棄」。
3. 下載失敗會殘留 `.part` 暫存檔，改為失敗時清掉。

### 未能在本次驗證的部分

- **Unity 編輯器內實際按 Play 的完整流程**：編輯器正開著專案，無法由此端操作，需要使用者按一次 Play 確認。
- **`PlayerBuild` 複製 `server/` 到建置版**：同樣需要在編輯器建置一次才能確認。

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
