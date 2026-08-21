# 系統實作結構

## 專案

```text
src/
├── NtmcScheduler.Contracts/
│   ├── Models.cs
│   └── Services.cs
├── NtmcScheduler.Infrastructure/
│   ├── Background/
│   ├── Csv/
│   ├── Data/
│   └── Services/
├── NtmcScheduler.Solvers/
│   ├── SolverContracts.cs
│   ├── MContracts.cs
│   ├── MSolver.cs
│   ├── MSolver.Input.cs
│   ├── MSolver.HardRules.cs
│   ├── MSolver.SoftRules.cs
│   ├── TContracts.cs
│   ├── TSolver.cs
│   ├── TSolver.Input.cs
│   ├── TSolver.HardRules.cs
│   └── TSolver.SoftRules.cs
├── NtmcScheduler.Cli/
│   └── Program.cs
└── NtmcScheduler.Web/
    ├── Components/
    ├── Services/
    └── Program.cs
```

`NtmcScheduler.Contracts` 只有 provider-neutral domain DTO 與 application service contracts，不參考 EF 或 OR-Tools。`Infrastructure` 實作 Identity／EF、CSV adapter、快照轉換、直接工作區驗證器與單一背景佇列。`Web` 是 .NET 10 Blazor Interactive Server，元件只呼叫 application services。CSV 使用 .NET `TextFieldParser`，CLI 與 Web 共用同一 adapter。

## Web 頁面與服務

- 共同頁：登入／修改密碼、Dashboard、需求填寫、共同設定版本、管理者帳號權限、只讀稽核紀錄。
- M 頁：員工主檔、建立班表、班表列表、萬年班表、互動寬表班表編輯器。
- T 頁：員工主檔、建立班表、班表列表、互動寬表班表編輯器。
- YM 頁：與 M 相同功能的獨立工作區，使用 Y06–Y19、獨立資料與權限。
- 每頁使用共用 `PageHelp` 顯示說明；編輯器固定人員欄、日期表頭、右側統計與底部 coverage 品質列。
- 所有寫入 service command 接收 `ActorContext` 與資源 revision token；服務層再次驗證工作區權限，資料與 AuditLog 在同一 transaction。
- Blazor circuit 內的 application service 不以 scoped `NtmcDbContext` 長期持有連線；每次操作經 `IDbContextFactory` 建立 context。帳號管理每個操作建立短生命週期 scope，讓 Identity 與 EF 共用同一 context。
- `ScheduleRunWorker` 單一讀取者執行 M／YM 共用的 `MSolver` 或 T 的 `TSolver`，從 immutable typed input JSON 重現；重啟恢復未完成工作且不重複處理終態 run。若 M 與 YM 後續規則不同，再依新決策調整或拆分 solver。

## 資料庫與 migration

- Identity、WorkspacePermission、Employee、不可變 ConfigurationRevision、DemandDraft／快照、EmployeeDemandSubmission、ScheduleRun、ScheduleVersion／assignments、AdoptedSchedule、M／YM 各自的全域 MPerpetualScheduleTemplate 與 append-only AuditLog（含 SessionId）。
- 所有 domain 主鍵與 revision token 使用應用程式產生 GUID；`AdoptedSchedule(Workspace, Month)` 主鍵保證每月唯一 `★`。
- repository-local `dotnet-ef` 管理兩套 provider-specific migration：現有歷史保留在 `NtmcScheduler.Migrations.Sqlite`，SQL Server 由 `NtmcScheduler.Migrations.SqlServer` 的 `InitialCreate` 起始。每次 model 變更必須對兩個 project 各新增一份 migration：

```bash
dotnet ef migrations add <Name> -p src/NtmcScheduler.Migrations.Sqlite -s src/NtmcScheduler.Web
NTMC_MIGRATION_PROVIDER=SqlServer dotnet ef migrations add <Name> \
  -p src/NtmcScheduler.Migrations.SqlServer -s src/NtmcScheduler.Web
```

## 資安邊界

- ASP.NET Core Identity 處理密碼雜湊、鎖定與 Cookie；頁面授權與 application service 授權同時執行。
- HTTPS/HSTS、antiforgery、嚴格 CSP、frame-ancestors none、nosniff、可信 forwarded proxy、持久化 Data Protection key ring。
- CSV 限 UTF-8、5 MB、固定表頭，先完整解析再 transaction 寫入；匯出對 spreadsheet formula 起始字元加單引號。
- Production 沒有 Data Protection 加密憑證即拒絕啟動；secret 由部署端注入，不放 repository。

## Solver 檔案責任

- `SolverContracts.cs`：共用的 `ScheduleInput`、月班表、日格、區間、選項、狀態與 Objective 輸出。無建模函數。
- `MContracts.cs` / `TContracts.cs`：候選與求解結果；M 另有外派輸出。
- `MSolver.cs` / `TSolver.cs`：公開 `Solve`、固定 Priority 群組的字典序 CP-SAT 呼叫、候選差異與結果讀取。M 與 YM 目前共用 `MSolver`，由 Infrastructure 傳入各自固定站點與班別時間；不建立重複的 `YMSolver`。未來規則若分歧，須另案更新規格、決策、實作與測試。
- `*.Input.cs`：快照複製、月份／人員／日格／區間驗證、歷史查詢與 R/R1 累積推導。
- `*.HardRules.cs`：OR-Tools 變數與不可關閉的硬限制。
- `*.SoftRules.cs`：軟違反量、固定群組及權重；M 為 J1 與直接加權合併的 `J4+J5`，T 為 J1–J5。

站務模型與 T 不共用任何建模邏輯。M 與 YM 因規則完全相同而共用站務模型；每個 solver 只有一個 private `Variables` record 收納 OR-Tools 變數，不建 `Variables/Constraints/Objectives/Candidates` class 層。

## CLI 責任

`NtmcScheduler.Infrastructure/Csv/ScheduleCsv.cs` 是 CSV 邊界：讀寫月班表，讀取八週區間、M 萬年班表與非常態班型，並把文字格值轉為 solver contracts。`Program.cs` 只做：

1. 詢問五個共用輸入；M 再詢問可留白的萬年班表 CSV。
2. 建立 `ScheduleInput`。
3. 依能力／T 月班別判斷 M 或 T。
4. 傳入 Ctrl+C cancellation token。
5. 顯示狀態與具名分數，寫出候選。

CLI 不包含業務規則，solver 不知道 CSV 路徑。

## 測試

`tests/NtmcScheduler.Solvers.Tests` 使用 MSTest，涵蓋 solver／CLI 與 Web Infrastructure。測試重點為：

- typed input 與 InvalidInput 邊界。
- M/T 至少一個可求解主要案例。
- CSV round-trip、舊表頭相容、M 萬年班表、BOM、quoted field、非常態班型、跨午夜 X 與非法格值。
- 新進人員、A/B 區間累積、八週區間驗證。
- timeout、cancellation、CLI redirected stdin 與 examples smoke test。
- 登入限流、設定版本凍結與驗證、service 工作區授權、revision token 及每月唯一 `★`。

## 可執行範例

`examples/m-2026-09` 與 `examples/t-2026-09` 都包含 `previous.csv`、`demand.csv`、`rest-intervals.csv`、`non-standard-shifts.csv` 與最小 README。兩組均由 solver 生成已驗證的合成需求，可直接交給 CLI 做快速 smoke test。範例假日不代表正式政府行事曆。

## 尚待部署環境驗收

部署步驟見 [`12-deployment.md`](12-deployment.md)。正式 Linux＋SQL Server migration、資料庫備份／還原、Data Protection X.509 憑證與 volume、journald／container driver 一年保留、反向代理可信來源，以及 Microsoft Playwright 端對端與基準規模互動測試，必須在部署環境取得連線與瀏覽器 runtime 後執行。
