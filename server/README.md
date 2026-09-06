# 中繼伺服器

大螢幕（Unity）與手機之間的訊息轉發站，同時託管手機網頁。
**這裡不含任何遊戲邏輯**——賠率、名次、籌碼全部在 Unity 端的純 C# Core，
伺服器只認得訊息的 `t` 欄位並決定要轉給誰。

## 啟動

需要 Node.js 18 以上。

```bash
cd server
npm install
npm start
```

啟動後：

- 手機網頁：`http://localhost:8080/`
- 大螢幕連線位址：`ws://localhost:8080/ws?role=host`（已是 `race.json` 的預設值）

## 手機要用 HTTPS 才有動作感測器

iOS Safari 與 Android Chrome 都只在**安全來源**下開放 `DeviceMotion`。
直接用區網 IP（`http://192.168.x.x:8080`）開會拿不到權限，iOS 甚至不會跳出詢問。

開發時最快的解法是用 Cloudflare 的臨時通道取得一個公開的 https 網址：

```bash
cloudflared tunnel --url http://localhost:8080
```

它會印出一個 `https://xxxx.trycloudflare.com` 網址，手機開那個就有動作感測器。

**沒有 HTTPS 也不會卡住**：手機頁上的「連打」按鈕與搖動走同一個計數器，
權限拿不到時照樣可以測完整流程。

## 目前範圍

原型階段只支援**單一房間**，房號路由留到 M3 正式版。
訊息協定定義在 `Assets/Scripts/Core/Protocol/Messages.cs`，
`public/app.js` 的欄位名必須與它完全一致，改協定時兩邊要同一個 commit 一起改。

## 部署

任何支援 WebSocket 長連線的平台都可以，程式碼不需修改，
伺服器會讀 `PORT` 環境變數。

**不能用 Firebase Hosting 或 Vercel Serverless**——它們不支援 WebSocket 長連線。
建議 Zeabur 或 Railway。免費層閒置會休眠，活動當天請提前十分鐘喚醒，
或升到不休眠的付費檔。

部署後把 `Assets/StreamingAssets/config/race.json` 的 `Network.RelayUrl`
改成 `wss://你的網域/ws?role=host` 即可，不必重新建置 Unity。
