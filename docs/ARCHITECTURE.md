# 賽馬遊戲 架構設計書

> 決策日期：2026-09-06
> 前提：雲端伺服器、同場 10～30 人、純娛樂籌碼（不落地儲存）

---

## 1. 總覽

三個角色，各自的職責界線很硬：

```
                    雲端中繼伺服器 (Node.js + ws)
                    +------------------------------+
                    | - 靜態網頁託管 (public/)      |
                    | - 房間路由 (roomCode -> 連線) |
                    | - 訊息轉發 (host <-> players) |
                    | - 最新快照快取（供重連回放）   |
                    | X 沒有任何遊戲規則             |
                    +---------------+--------------+
                  WSS               |        WSS + HTTPS
        +-------------------------- + ------------------+
        |                                               |
+-------v-----------------+              +--------------v--------+
| Unity Windows 大螢幕     |              | 手機瀏覽器 x 10～30    |
| ---- 唯一權威 ----       |              |                       |
| - RaceEngine 賽事模擬    |              | - 掃 QR 進場           |
| - OddsCalculator 賠率    |              | - 下注                 |
| - BettingBook 籌碼帳本   |              | - 賽中丟道具            |
| - GameLoop 階段狀態機    |              | - 看名次與自己的錢包    |
| - 3D 賽道視覺與播報      |              | 純 DOM，無框架          |
+-------------------------+              +-----------------------+
```

**為什麼把權威放在 Unity 而不是雲端？**

1. 遊戲邏輯全部留在 C#，你最熟的語言，也最好測（`tools/CoreTests` 不用開 Unity 就能跑）
2. 大螢幕的動畫流暢度只取決於本機，不受網路抖動影響——這是觀眾唯一在看的東西
3. 伺服器變成可拋棄品：換部署商、換方案、伺服器重啟，都不影響玩法程式碼
4. 純娛樂籌碼，沒有防作弊的強需求，不需要伺服器端權威

代價：Unity 主機斷線 = 整場停擺。所以 Net 層的重連要做扎實（見 §6）。

---

## 2. 執行期資料流

### 一場比賽的完整時序

| 階段 | Unity 做什麼 | 手機看到什麼 |
|---|---|---|
| **Idle** | 展示上一場結果與排行 | 「等待下一場開始」 |
| **Betting** | 跑蒙地卡羅算賠率 → 廣播 `phase` + 賠率表；接收並記錄下注 | 四匹馬卡片、賠率、籌碼滑桿、下注鈕 |
| **Racing** | 50 Hz 推進模擬，10 Hz 廣播 `snapshot`；接收並套用道具 | 即時名次條、道具按鈕（含冷卻圈） |
| **Photo** | 衝線特寫、慢動作、名次揭曉 | 「結果揭曉中…」 |
| **Settle** | 結算派彩 → 廣播 `result` + 各自 `wallet` | 本場輸贏、新籌碼餘額 |

### 執行緒模型

```
WebSocket 背景執行緒  ->  ConcurrentQueue<InboundMessage>
                                 |
Unity 主執行緒 Update()  --------+
   1. 消化佇列裡的所有訊息 -> 交給 GameLoop
   2. GameLoop.Tick(deltaTime) -> 內部固定 50 Hz 累加器推進 RaceEngine
   3. View 讀取 RaceEngine 快照 -> 對位置做插值渲染
   4. 到了廣播節拍（10 Hz）-> 打包 snapshot 送出
```

**鐵則**：網路回呼裡不碰 Unity API、不碰 `RaceEngine`，只能 Enqueue。

---

## 3. Core 模組（純 C#，禁 UnityEngine）

```
Assets/Scripts/Core/
├─ Config/
│  ├─ RaceConfig.cs        賽道長度、tick 頻率、下注秒數、初始籌碼、抽水率
│  ├─ HorseConfig.cs       名稱、顏色、基礎速度、爆發力、耐力、穩定度
│  └─ ItemConfig.cs        道具效果倍率、持續時間、冷卻、費用、每場次數上限
├─ Protocol/
│  ├─ PhaseMessage.cs      DTO：階段、結束時間戳、賠率表
│  ├─ SnapshotMessage.cs   DTO：tick、各馬進度與名次
│  ├─ WalletMessage.cs     DTO：餘額、注單、道具冷卻
│  ├─ ResultMessage.cs     DTO：名次、派彩明細
│  └─ InboundMessage.cs    DTO：join / bet / item
├─ RaceEngine.cs           賽事模擬，固定步長，可 Clone
├─ OddsCalculator.cs       蒙地卡羅算勝率 -> 賠率（純函式）
├─ BettingBook.cs          注單、餘額、派彩結算
├─ ItemSystem.cs           道具效果、冷卻、次數驗證
├─ RoomState.cs            玩家名冊、連線狀態
└─ GameLoop.cs             階段狀態機，唯一的對外進入點
```

### RaceEngine 的模擬模型

每匹馬每個 tick：

```
targetSpeed = baseSpeed
            * staminaCurve(progress)       // 耐力曲線：後段掉速
            * burstFactor(rng, stability)  // 隨機爆發，穩定度越高波動越小
            * itemMultiplier               // 道具疊加後的倍率

speed += (targetSpeed - speed) * accel * dt   // 平滑逼近，避免瞬間跳速
progress += speed * dt
```

- `System.Random(seed)`，seed 每場記錄下來，可完整重播
- 固定步長 `dt = 1/50`，`Tick(elapsed)` 用累加器消化，多餘的時間留到下一幀
- `Clone()` 提供給 `OddsCalculator` 做模擬，**絕不改動真實賽況**

### 賠率怎麼算

賽前把 `RaceEngine` clone 出來跑 2000 次（不含道具），統計各馬勝率：

```
odds[i] = (1 - takeRate) / winRate[i]
```

- 下限鎖 1.05（避免出現賠率小於 1）
- 上限鎖 20.0（避免弱馬賠率爆表）
- 2000 次 x 4 匹馬的模擬在 PC 上約 10～30 ms，可以在 Idle → Betting 轉換時同步跑完

**這是固定賠率，不是同注分彩**。理由：同注分彩在 10～30 人的小場次容易出現「大家都押同一匹，賠率剩 1.1」的無趣狀況；固定賠率讓弱馬永遠有吸引力。抽水率設 15%（`RaceConfig.TakeRate`）當作平衡閥門。

---

## 4. 道具系統設計

使用者原始構想是「加速」與「讓馬直接停下來」。**建議把「直接停下來」改成「短暫減速」**：

> 完全靜止會讓被針對的那匹馬幾乎必輸，押那匹馬的玩家等於被單方面剝奪，
> 在 10～30 人的小場子裡很容易變成互相報復，體驗會壞掉。

建議的初版配置（全部寫在 `ItemConfig`，現場可調）：

| 道具 | 效果 | 持續 | 冷卻 | 費用 |
|---|---|---|---|---|
| 加速 Boost | 目標馬速度 x1.35 | 2.0 s | 8 s | 50 籌碼 |
| 絆腳 Slow | 目標馬速度 x0.60 | 2.0 s | 8 s | 50 籌碼 |

附加規則：

- 只在 Racing 階段可用
- 每人每場總共 2 次
- 同一匹馬同時最多疊 2 層（避免全場圍剿一匹）
- 大螢幕要有明顯的視覺與音效回饋，並顯示「誰對誰用了什麼」——這是全場最好笑的部分，不能藏起來

---

## 5. 訊息協定

WebSocket，JSON 文字幀。所有訊息都是 `{ "t": "<type>", ... }`。

### Host → Players

```jsonc
// 階段變更
{ "t":"phase", "phase":"betting", "endsAt":1757145600000, "race":12,
  "horses":[{"id":0,"name":"閃電","color":"#E74C3C","odds":2.4}] }

// 賽況快照（賽中 10 Hz）
{ "t":"snap", "tick":420, "p":[0.42,0.38,0.45,0.31], "r":[2,3,1,4] }

// 個人錢包（只送給該玩家）
{ "t":"wallet", "balance":850, "bets":[{"horse":0,"amount":100}],
  "itemsLeft":2, "cooldownUntil":1757145612000 }

// 結果
{ "t":"result", "order":[2,0,3,1],
  "payout":{"me":240,"delta":140}, "top":[{"name":"阿明","balance":1420}] }
```

### Players → Host

```jsonc
{ "t":"join", "nick":"阿明", "pid":"localStorage 保存的 id 或 null" }
{ "t":"bet",  "horse":2, "amount":100 }
{ "t":"item", "kind":"boost", "horse":0 }
```

### 協定紀律

- 欄位名縮短是刻意的：30 人 x 10 Hz 的快照要壓頻寬
- **DTO 定義在 `Core/Protocol/`，手機端 JS 的欄位名必須完全一致**，協定變更兩邊同一個 commit 一起改
- 倒數一律傳 `endsAt`（Unix 毫秒），手機端自己遞減
- 伺服器只認 `t` 欄位做路由，其餘內容原封不動轉發

---

## 6. 連線與容錯

這是現場活動最容易翻車的地方，優先級高於美術。

| 情境 | 處理方式 |
|---|---|
| 手機息屏後回來 | `visibilitychange` → 重連 → 送 `join` 帶原 `pid` → Host 回完整 `phase` + `wallet` |
| 手機網路瞬斷 | 前端指數退避重連（0.5s→1s→2s→5s 上限），畫面顯示「重新連線中」遮罩 |
| 玩家重整頁面 | `pid` 存在 `localStorage`，身分與籌碼原樣回來 |
| **Unity 主機斷線** | Net 層自動重連；重連前大螢幕顯示明顯警示；比賽照跑不中斷（模擬在本機） |
| 伺服器重啟 | 房間快取消失 → Host 重連後立刻重推一次完整狀態；玩家自動重連並重新 `join` |
| 玩家送壞封包 | try/catch 只踢那條連線，記 log，**絕不影響賽事** |
| 免費層冷啟動 | 活動前 10 分鐘手動開一次頁面喚醒；正式場建議升到不休眠的付費檔 |

---

## 7. 技術選型

| 項目 | 選擇 | 理由 |
|---|---|---|
| Unity WebSocket 客戶端 | **NativeWebSocket** (MIT, UPM git URL) | 輕、無原生外掛、Windows 與 WebGL 都能用 |
| JSON 序列化 | Unity 內建 `JsonUtility` | 夠用、零依賴；若遇到多型需求再換 |
| QRCode 產生 | **ZXing.Net** (Apache-2.0) | 純 C#，放 `Assets/Plugins/` |
| 伺服器 | **Node.js + `ws` + `express`** | 最小依賴，中繼站只需要這兩個 |
| 手機前端 | 原生 HTML/CSS/JS，單頁 | 免建置流程，改完直接部署 |
| 部署平台 | **Zeabur** 或 **Railway** | 支援 WebSocket 長連線、部署簡單 |

**部署平台注意**：Firebase Hosting 與 Vercel Serverless **不支援 WebSocket 長連線**，不能用。
Render 免費層閒置 15 分鐘會休眠、冷啟動約 30 秒。
活動當天建議用最低付費檔（約 US$5/月）換取不休眠，這錢很值得。

---

## 8. 里程碑

| # | 目標 | 驗收標準 |
|---|---|---|
| **M0** | 專案骨架、CLAUDE.md、架構書、git 初始化 | 文件齊、repo 乾淨 |
| **M1** | Core 賽事引擎 + CoreTests | `dotnet run` 全綠；同 seed 結果可重現 |
| **M2** | 大螢幕視覺：賽道、4 匹膠囊、攝影機跟拍、名次板 | 按空白鍵能完整跑完一場並顯示名次 |
| **M3** | 雲端中繼站 + 手機頁面 + QRCode 進場 | 兩支手機掃碼能進場、看到自己的暱稱出現在大螢幕 |
| **M4** | 下注與賠率結算 | 完整跑一輪 Betting → Racing → Settle，籌碼正確增減 |
| **M5** | 道具系統 | 手機丟道具，大螢幕即時有反應與特效 |
| **M6** | 音效、播報、正式素材替換、現場壓力測試 | 30 條模擬連線壓測不掉幀、不斷線 |

**M1～M2 完全不需要網路**，可以先把最好玩的部分（賽事本身）做出來看效果，再接連線。

---

## 9. 已知風險

1. **現場網路** — 雲端方案下，觀眾用 4G 或現場 Wi-Fi 都行，但場館訊號差時會出現大量重連。
   前端的重連遮罩與樂觀 UI（按下去先變灰，收到確認再定案）要做好。
2. **免費層休眠** — 見 §7。
3. **Unity 主機是單點故障** — 純娛樂場合可接受，但主機的網路建議走有線。
4. **道具平衡** — 首次現場測試後幾乎一定要調參數，所以參數必須能不重編譯就改
   （`RaceConfig` 走 ScriptableObject 或外部 JSON）。
