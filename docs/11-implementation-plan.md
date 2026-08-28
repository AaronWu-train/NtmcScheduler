# 11. 開發指南與實作結構

本文件提供從規格定位、修改實作到驗證交付的最短路徑。業務規則以 [`01`–`09`](01-scope-and-workflow.md) 與 [`tex/main2.tex`](tex/main2.tex) 為準，決策沿革集中在 [`10-decisions.md`](10-decisions.md)；遇到未定義的業務情況，先回報規格缺口，不自行補上假設。

## 1. 開始開發

需要 .NET 10 SDK。從 repository 根目錄執行：

```bash
dotnet tool restore
dotnet restore NtmcScheduler.slnx
dotnet build NtmcScheduler.slnx
```

開發環境預設使用 SQLite。建立首位管理者並啟動 Web：

```bash
dotnet run --project src/NtmcScheduler.Web -- --init-admin admin
dotnet run --project src/NtmcScheduler.Web
```

啟動位址為 `https://localhost:7189` 與 `http://localhost:5109`。本機 SQLite、Data Protection keys 與 `appsettings.Development.json` 已由 `.gitignore` 排除；不得提交密碼、連線字串、CLI 求解輸出、真實人員資料或正式班表。

## 2. 修改前先定位真相來源

| 修改內容 | 先讀文件 | 主要程式入口 | 主要測試 |
|---|---|---|---|
| CSV 欄位、日格與歷史 | [`02`](02-glossary.md)、[`03`](03-data-and-validation.md) | `src/NtmcScheduler.Infrastructure/Csv/ScheduleCsv.cs`、相關 service | `MSolverTests`、`TSolverTests`、`WebInfrastructureTests` |
| 硬規則 | [`04`](04-hard-rules.md)、[數學模型](tex/main2.tex) | `src/NtmcScheduler.Solvers/*Solver.HardRules.cs` | `MSolverTests`、`TSolverTests` |
| 軟規則、Priority、權重 | [`05`](05-soft-rules.md)、[數學模型](tex/main2.tex) | `src/NtmcScheduler.Solvers/*Solver.SoftRules.cs` | Solver tests、rule-definition tests |
| 求解狀態與候選 | [`06`](06-solver-and-output.md) | `src/NtmcScheduler.Solvers/*Solver.cs`、`src/NtmcScheduler.Infrastructure/Background/ScheduleRunWorker.cs` | Solver tests、worker tests |
| 資料庫與 application service | [`07`](07-architecture.md) | `src/NtmcScheduler.Contracts/Services.cs`、`src/NtmcScheduler.Infrastructure/Data`、`src/NtmcScheduler.Infrastructure/Services` | `WebInfrastructureTests` |
| Blazor 畫面與文字 | [`08`](08-frontend.md) | `src/NtmcScheduler.Web/Components/Pages`、`src/NtmcScheduler.Web/Components/Shared` | `WebInfrastructureTests` 加登入後瀏覽器驗收 |
| 驗收案例 | [`09`](09-acceptance.md) | 對應功能 | 對應自動化測試與人工驗收 |
| 正式環境 | [`12`](12-deployment.md) | `src/NtmcScheduler.Web/Program.cs`、`rebuild_and_deploy.sh` | publish、migration、部署環境驗收 |

若文件、測試與程式互相衝突，先釐清適用工作區、月份、資料來源與目前 branch；不要用放寬驗證或改 fixture 掩蓋規格差異。

## 3. 標準修改流程

1. 執行 `git status --short`，分辨既有 staged／unstaged 修改與本次範圍；不要覆蓋其他人的變更。
2. 沿上表讀取規格、介面、實作與既有測試，確認 UI、DTO、service、資料庫與 solver 的實際資料流。
3. 修改規則行為時，先更新對應規格與 `tex/main2.tex`，並在 `10-decisions.md` 文末追加決策，再修改程式與測試。
4. 只改完成需求所需的最少檔案；共用路徑已有實作時直接沿用，不新增規則 catalog、encoder、Rule ID map 或單一實作的抽象層。
5. 先跑最接近變更的測試，再跑 solution build 與完整測試；需要登入狀態的 UI 流程另做瀏覽器驗收。
6. 交付前檢查 `git diff --check`、`git diff` 與 `git status --short`，確認沒有真實資料、secret、產物或無關修改。

純 Markdown 或 TeX 修改可依文件類型做格式、連結、編譯與版面驗證，不必為未動到的程式宣稱完成 .NET 驗收。

## 4. 專案與相依邊界

```text
src/
├── NtmcScheduler.Contracts/             provider-neutral DTO 與 application service 介面
├── NtmcScheduler.Solvers/               OR-Tools、M/T 模型與求解契約
├── NtmcScheduler.Infrastructure/        EF Core、CSV、service、稽核與背景工作
├── NtmcScheduler.Migrations.Sqlite/     SQLite migrations 與 model snapshot
├── NtmcScheduler.Migrations.SqlServer/  SQL Server migrations 與 model snapshot
├── NtmcScheduler.Cli/                   CLI 入口
└── NtmcScheduler.Web/                   Blazor UI、Identity 與 HTTP 入口
tests/
└── NtmcScheduler.Solvers.Tests/         Solver、CLI、Infrastructure 與 Web 測試
```

必須維持以下邊界：

- `Contracts` 不參考 EF Core 或 OR-Tools；含 `CpModel`、`BoolVar`、`IntVar`、`LinearExpr` 的介面只能位於 `Solvers`。
- Blazor 元件只呼叫 application service，不直接建立 OR-Tools 模型或操作 `NtmcDbContext`。
- Infrastructure 將資料庫與 CSV 轉為 typed solver input；solver 不知道 CSV 路徑、HTTP、Identity 或 EF entity。
- M／YM 共用 `MSolver`，T／YT 共用 `TSolver`；M 與 T 保持分離且明白的 source-as-spec partial 檔。
- 程式使用有意義的英文規則與違反量名稱，不保存文件用的短 Rule ID；UI 與輸出使用繁體中文說明。
- 所有時間以台北時間處理，夜班與跨午夜 X 歸開始日期。

## 5. 實作導覽

### Web 與 application service

- `Web/Program.cs`：主機、Identity、授權、CSP、下載端點、資料庫與 DI。
- `Web/Components/Pages`：頁面；`Components/Shared`：共用導覽與說明。
- `Contracts/Models.cs`、`Services.cs`：UI 與 Infrastructure 共用的 DTO、command 與 service 介面。
- `Infrastructure/Services`：授權後的讀寫、revision token、transaction、AuditLog 與 solver 呼叫。
- `Infrastructure/Background`：單一求解佇列、取消與重啟恢復。

Blazor circuit 生命週期長，application service 每次資料庫操作都透過 `IDbContextFactory<NtmcDbContext>` 建立新 context。Identity 帳號操作則以短生命週期 scope 讓 `UserManager`、EF 與 transaction 共用同一 context。

### Solver

- `SolverContracts.cs`：`ScheduleInput`、月班表、日格、區間、選項、狀態與 Objective 輸出，不含建模函數。
- `MContracts.cs`、`TContracts.cs`：候選與結果契約；M 另含外派輸出。
- `MSolver.cs`、`TSolver.cs`：公開 `Solve`、字典序求解、候選差異與結果讀取。
- `*.Input.cs`：輸入複製、月份／歷史／區間驗證與跨月累積推導。
- `*.HardRules.cs`：OR-Tools 變數與不可關閉的硬限制。
- `*.SoftRules.cs`：具名違反量、固定 Priority 群組與權重。

不要因測試時間或單一 fixture 無解而放寬硬限制、時限或斷言。`TimeLimit` 可帶合法 incumbent，與 `Infeasible` 必須分開處理；`ObjectiveScore` 也不代表每個 Priority 都已證明最佳。

### CSV 與 CLI

`Infrastructure/Csv/ScheduleCsv.cs` 是文字格式與 typed model 的邊界。修改 CSV 時同時核對 parser、Web 上傳／下載 service、範本、實際 HTTP 回應與 round-trip 測試；畫面接受上傳不等於資料可供 solver 正常求解。

`Cli/Program.cs` 只負責互動輸入、建立 `ScheduleInput`、選擇 M/T、傳遞取消信號及寫出候選。可用以下範例做 smoke test：

```bash
cd examples/m-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli

cd ../t-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli
```

## 6. 資料庫與 migrations

EF model 位於 `Infrastructure/Data/Entities.cs` 與 `NtmcDbContext.cs`。SQLite、SQL Server 使用獨立 migration project；每次 model 變更必須產生語意相同的兩份 migration：

```bash
dotnet tool restore
dotnet ef migrations add <Name> \
  -p src/NtmcScheduler.Migrations.Sqlite \
  -s src/NtmcScheduler.Web

NTMC_MIGRATION_PROVIDER=SqlServer dotnet ef migrations add <Name> \
  -p src/NtmcScheduler.Migrations.SqlServer \
  -s src/NtmcScheduler.Web
```

新增後逐一檢查 migration、model snapshot 與 SQL，不以其中一個 provider 成功推論另一個也正確。查詢必須維持 provider-neutral；資料變更與 AuditLog 必須在同一 transaction。

安全性或批次匯入相關寫入要沿用 Identity、工作區權限、revision token 與 AuditLog。批次 CSV 必須先完整驗證再一次提交；任一列失敗時整批回滾，密碼與 CSV 原文不得進入 Log。

## 7. 驗證層級

| 變更 | 最低驗證 |
|---|---|
| Markdown | `git diff --check`、本機連結存在、內容與權威規格一致 |
| `docs/tex/main2.tex` | XeLaTeX 編譯、warning 檢查、PDF 同步與頁面版面檢視 |
| Solver／CSV | 對應 `MSolverTests` 或 `TSolverTests`，再 build solution |
| Service／EF／Identity | `WebInfrastructureTests`，再 build solution |
| EF model | 兩套 migration 與 snapshot 檢查、相關 persistence tests |
| Blazor UI | build、相關 service tests、登入後瀏覽器操作與重新載入確認 |
| 交付前完整驗證 | `dotnet test NtmcScheduler.slnx` |

常用指令：

```bash
dotnet test NtmcScheduler.slnx --filter FullyQualifiedName~MSolverTests
dotnet test NtmcScheduler.slnx --filter FullyQualifiedName~TSolverTests
dotnet test NtmcScheduler.slnx --filter FullyQualifiedName~WebInfrastructureTests
dotnet build NtmcScheduler.slnx
dotnet test NtmcScheduler.slnx
```

Solver 測試刻意不平行執行，完整案例可能超過一分鐘。若 sandbox 出現 `SocketException (13): Permission denied` 或 named-pipe 錯誤，先在允許本機 IPC 的環境重跑；這類環境錯誤不是程式測試失敗。

Build 與測試通過不代表登入後 UI、下載內容或正式 SQL Server 部署已驗收。CSV 下載需檢查實際回應位元組；畫面設定需追到 DTO、service、資料庫，再用新的 DbContext 或重新載入確認 round-trip persistence。

## 8. 交付檢查

- 規格、數學模型、程式、測試與 UI 文字同步，且沒有未定義的業務假設。
- 僅修改本次任務所需檔案，保留工作樹既有變更與原始檔案編碼。
- Contracts、solver、Blazor 與資料庫相依邊界未被破壞。
- Schema 變更包含 SQLite 與 SQL Server migrations；安全敏感寫入保留授權、transaction 與稽核。
- 說明實際執行的驗證、未執行的瀏覽器／部署驗收及任何環境限制，不把 build 當成完整驗收。
