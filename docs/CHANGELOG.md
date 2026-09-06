# 變更紀錄

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
