# 新北捷人員排班系統（NtmScheduler）

新北捷運內部人員排班：輸入人員、指定休假（R\*）、公務事件（X）與歷史班表後，以 OR-Tools CP-SAT 產生月班表候選（最多 3 份），挑選為 Draft、人工修改，通過全部硬規則後發布為 Published。

第一版範圍：**站務 M**（12 站）與**檢測 T**（月班組輪值）。無登入；頂欄「目前操作者」寫入稽核（之後可接公司 AD）。

## 規格（唯一真相來源）

| 文件 | 內容 |
|---|---|
| [`AGENTS.md`](AGENTS.md) | 總覽、技術棧、Agent 守則 |
| [`docs/01`–`10`](docs/) | 範圍、名詞、資料驗證、硬／軟規則、求解、架構、前端、驗收、決策 |
| [`docs/11-implementation-plan.md`](docs/11-implementation-plan.md) | 實作架構與里程碑 |
| [規格書 PDF v6](docs/新北捷人員排班系統_完整開發規格書_v6.pdf) | 原始規格；與 Markdown／決策衝突時以 Markdown 為準 |

Rule ID（如 `GEN-H-02`、`T-S-ABILITY`）一經定義即固定，程式／DB／UI／匯出必須一致。

## 技術棧

| 項目 | 決定 |
|---|---|
| 執行環境 | .NET 10 |
| UI | Blazor Web App（Interactive Server）＋ Bootstrap |
| 求解 | Google OR-Tools CP-SAT（M／T 分開建模） |
| 資料庫 | EF Core；開發 SQLite，正式環境 PostgreSQL／SQL Server（未定案，禁止 provider 專屬語法） |
| 測試 | MSTest；E2E 預留 Microsoft.Playwright |
| 套件 | 僅 Microsoft 與 Google 官方套件 |

## 專案結構

```
NtmScheduler.slnx
src/NtmScheduler.Core/            Domain、曆法、26 條規則評估、InputValidator、服務介面
src/NtmScheduler.Solvers/         CP-SAT ModelBuilder、字典序引擎、缺班分析（可參考 OR-Tools）
src/NtmScheduler.Infrastructure/  EF Core、CSV、應用服務、ScheduleRunWorker
src/NtmScheduler.Web/             Blazor 頁面與互動寬表
tests/NtmScheduler.Tests/         Core／Solvers／Integration；E2E 占位
```

相依規則：`Core` 不得參考 OR-Tools 或 EF Core；Blazor 不直接建 CP-SAT 模型。

## 主要流程

1. 維護人員、月班組（T）、X／R\* 事件、排班週期與規則開關／順序  
2. 匯入歷史班表（不足 8 週無法排班，見 D-05）  
3. 建立 `ScheduleRun` → 背景 Worker 求解 → 最多 3 份候選  
4. 選一份為 Draft → 寬表編輯 → 通過全部 P0 → 發布 Published  
5. 匯出 `schedule.csv`／coverage／violations；查看稽核與缺班分析  

## 啟動

需求：.NET 10 SDK。

```bash
dotnet restore NtmScheduler.slnx
dotnet build NtmScheduler.slnx
dotnet test tests/NtmScheduler.Tests/NtmScheduler.Tests.csproj
dotnet run --project src/NtmScheduler.Web
```

瀏覽器開啟顯示的本機 URL（見 `Properties/launchSettings.json`）。開發資料庫：`ntm.db`（SQLite；`*.db` 已列入 `.gitignore`）。

## 頁面（繁體中文 UI）

| 路徑用途 | 說明 |
|---|---|
| 人員／事件／月班組／週期／規則 | 輸入與設定 |
| 匯入 | 歷史 `schedule.csv`＋`events.csv` |
| 求解執行／詳情／候選比較 | Run 進度、候選差異 |
| Draft 寬表編輯 | 儲存格編輯、違規與 coverage 面板 |
| 版本／缺班／稽核 | 已發布版本、人力缺口、操作紀錄 |

## 重現性

每次 Run 保存 seed、規則順序與輸入快照。相同快照＋相同 seed 應得到相同結果；嚴格位元級重現見實作計畫中的 OR-Tools deterministic 設定。

## 驗收與已知限制

- 測試命名含 AC 編號時對應 [`docs/09-acceptance.md`](docs/09-acceptance.md)。目前 `dotnet test` 約 41 項（Core／Solvers／Integration）。
- Playwright E2E 仍為占位（`tests/.../E2E/SmokePlaceholderTests.cs`，建置時排除）。
- 建置可能出現 `SQLitePCLRaw.lib.e_sqlite3` 的 NU1903 警告（套件傳遞相依）；不影響目前功能編譯。
- Draft 編輯時部分即時休假統計／P0 選項可再加厚（見 `Core/Abstractions/STUBS.md`）。

時間一律台北時間（UTC+8）；夜班歸屬其開始日期。
