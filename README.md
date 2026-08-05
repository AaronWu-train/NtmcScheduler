# NtmScheduler

新北捷運人員排班系統。依人員資料、指定休假、公務事件與歷史班表，以 OR-Tools CP-SAT 產生月班表候選，經人工調整後發布。

目前支援站務（M）與檢測（T）兩個單位。

## 環境需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 快速開始

```bash
git clone https://github.com/AaronWu-train/employee_scheduling.git
cd employee_scheduling

dotnet run --project src/NtmScheduler.Web --launch-profile http
```

瀏覽器開啟 http://localhost:5109。

開發環境使用 SQLite，首次啟動會自動建立 `ntm.db`。

## 建置與測試

```bash
dotnet build NtmScheduler.slnx
dotnet test tests/NtmScheduler.Tests/NtmScheduler.Tests.csproj
```

## 專案結構

```
src/NtmScheduler.Core/             領域模型與規則評估
src/NtmScheduler.Solvers/          CP-SAT 求解
src/NtmScheduler.Infrastructure/   資料庫、CSV、背景工作
src/NtmScheduler.Web/              Blazor UI
tests/NtmScheduler.Tests/          測試
docs/                              規格與決策紀錄
```

## 技術棧

- ASP.NET Core Blazor（Interactive Server）
- Google OR-Tools CP-SAT
- EF Core + SQLite（開發）

## 目前缺口

- Run 的 `SnapshotJson` 目前只有中繼資料；Worker 仍讀取即時資料庫，尚未做到可重現的完整輸入快照與 Published 歷史銜接。
- Draft 的完整 P0／Coverage／軟規則驗證與逐格選項預檢尚未實作；目前採 fail-closed，不能發布未驗證班表。
- M 缺班詳情與 T 衝突摘要尚未完整接上 UI。
- 尚無真正的瀏覽器 E2E 測試；已移除原本永遠通過的 placeholder。

## 文件

業務規則與資料格式見 [`docs/`](docs/)。Agent 相關說明見 [`AGENTS.md`](AGENTS.md)。
