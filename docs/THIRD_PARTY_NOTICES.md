# 第三方元件與授權

本專案只採用 MIT / Apache-2.0 / CC0 授權的元件。
**不使用 GPL 與 CC BY-SA**——share-alike 條款對商業活動有風險。

---

## ZXing.Net 0.16.9

- 用途：大螢幕上產生讓觀眾掃描入場的 QRCode
- 授權：Apache License 2.0
- 來源：https://github.com/micjahn/ZXing.Net/
- 檔案：`Assets/Plugins/ZXing/zxing.dll`（netstandard2.0 版本）

取用 netstandard2.0 而非更高版本，是為了與 Unity 2022 的
.NET Standard 2.1 相容性層級保持最大相容。

---

## ws 8.x

- 用途：中繼伺服器的 WebSocket 實作
- 授權：MIT
- 來源：https://github.com/websockets/ws
- 位置：`server/node_modules/`（不進版控，由 `npm install` 取得）
- 建置版會把 `node_modules/` 一起複製到 exe 旁的 `server/`，其中包含 ws 的 LICENSE 檔，符合 MIT 的標示要求

---

## cloudflared（Cloudflare Tunnel 用戶端）

- 用途：大螢幕自動開啟 Cloudflare 臨時通道（Quick Tunnel），讓手機用公開的 https 網址連進本機中繼伺服器
- 授權：Apache License 2.0
- 來源：https://github.com/cloudflare/cloudflared
- **不隨專案散布**：第一次使用時由 `Net/CloudflaredInstaller` 從官方 GitHub Release 下載到
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\HorseRace\tools\cloudflared.exe`；
  也可以在 `race.json` 的 `Network.CloudflaredPath` 指定自行安裝的版本
- 服務條款注意：trycloudflare.com 的臨時通道是 Cloudflare 提供給測試與開發用的免費服務，
  **沒有可用性保證**。因此通道失敗時 QRCode 會自動退回區網網址（見 `docs/ARCHITECTURE.md` 第 6 節）

---

## 字型

大螢幕的中文字型是執行期向作業系統索取的（微軟正黑體等），
**不隨專案散布任何字型檔案**，因此沒有字型授權問題。
若目標機器沒有中文字型，`FontProvider` 會退回 Unity 內建字型並在 log 中警告。

---

## 美術素材

目前全部為程式生成（基本幾何體 + 執行期建立的材質），**沒有使用任何外部素材**。
未來若需要免費素材，優先採用 Kenney.nl（CC0）。
