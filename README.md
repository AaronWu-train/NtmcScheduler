# NtmScheduler

新北捷運人員排班系統。依人員資料、指定休假、公務事件與歷史班表，以 OR-Tools CP-SAT 產生月班表候選，經人工調整後發布。

目前支援站務（M）與檢測（T）兩個單位。

## 環境需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 快速開始

```bash
git clone https://github.com/AaronWu-train/employee_scheduling.git
cd employee_scheduling

dotnet restore NtmScheduler.slnx
dotnet run --project src/NtmScheduler.Web
```

瀏覽器開啟 https://localhost:7036（或 http://localhost:5109）。

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

## 文件

業務規則與資料格式見 [`docs/`](docs/)。Agent 相關說明見 [`AGENTS.md`](AGENTS.md)。
