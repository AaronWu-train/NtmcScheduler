# 程式導覽

依「我要改什麼／找什麼」對應實際路徑。專案維持三層：`Core`（領域＋規則，不碰 OR-Tools／EF）、`Solvers`（CP-SAT）、`Infrastructure`＋`Web`（資料庫、服務、UI）。

## 進入點與畫面

| 項目 | 路徑 |
|---|---|
| 啟動 | `src/NtmScheduler.Web/Program.cs` |
| 導覽列 | `src/NtmScheduler.Web/Components/Layout/NavMenu.razor` |
| 首頁／載入範例 | `.../Pages/Home.razor` |
| 人員 | `.../Pages/Employees.razor` |
| T 月班組 | `.../Pages/Shifts.razor` |
| R*／X（含白話說明） | `.../Pages/Events.razor` |
| 8 週週期 | `.../Pages/Cycles.razor` |
| 規則管理 | `.../Pages/Rules.razor` |
| 建立／列表 Run | `.../Pages/Runs.razor` |
| Run 進度 | `.../Pages/RunDetail.razor` |
| 候選比較 | `.../Pages/Candidates.razor` |
| 目前班表寬表 | `.../Pages/ScheduleEditor.razor` |
| 快照 | `.../Pages/Versions.razor` |
| CSV 工具 | `.../Pages/Import.razor` |
| 寬表元件 | `.../Shared/WideTable.razor` |

## 資料庫

| 項目 | 路徑 |
|---|---|
| DbContext | `src/NtmScheduler.Infrastructure/Data/NtmDbContext.cs` |
| 實體 | `.../Data/Entities/`（`MonthSchedule`、`ScheduleSnapshot`、`Assignment`、`Employee`、`FixedEvent`…） |
| 遷移 | `.../Data/Migrations/` |

`Assignment.OwnerType`：`Candidate`／`Schedule`／`Snapshot`。

## 求解

| 項目 | 路徑 |
|---|---|
| 背景 Worker | `src/NtmScheduler.Infrastructure/Background/ScheduleRunWorker.cs` |
| 組 SolveRequest／歷史 | `.../Services/ScheduleContextBuilder.cs` |
| 求解入口 | `src/NtmScheduler.Solvers/SolveService.cs` |
| 字典序引擎 | `.../LexicographicSolveEngine.cs` |
| M 模型 | `.../M/MModelBuilder.cs` |
| T 模型 | `.../T/TModelBuilder.cs` |
| 硬約束編碼 | `.../Common/HardConstraintEncoder.cs` |

流程：硬規則進 CP-SAT constraint → 軟規則依 `Order` 逐條 `Minimize` → 固定目標值 → 下一條 → 最多 3 份差異候選。

## 規則

| 項目 | 路徑 |
|---|---|
| 目錄（ID、名稱、說明、預設順序） | `src/NtmScheduler.Core/Evaluation/RuleCatalog.cs` |
| 評估引擎 | `.../RuleEvaluationEngine.cs` |
| 硬規則 | `.../Rules/HardRules.cs` |
| 共用軟規則 | `.../Rules/GeneralSoftRules.cs` |
| M 軟規則 | `.../Rules/MSoftRules.cs` |
| T 軟規則 | `.../Rules/TSoftRules.cs` |

修改軟規則請先看 [`13-soft-rules-guide.md`](13-soft-rules-guide.md)。

## 應用服務

| 功能 | 服務 |
|---|---|
| 目前班表／選候選／編輯／驗證／快照 | `Infrastructure/Services/ScheduleService.cs` |
| Run 建立與進度 | `ScheduleRunService.cs` |
| 候選列表 | `CandidateService.cs` |
| 人員／事件／週期／規則 | `EmployeeService`、`EventService`、`ScheduleCycleService`、`RuleSettingService` |
| 範例資料 | `SampleData/DemoDataSeeder.cs`（資料定義在 `Core/SampleData/DemoDataset.cs`） |

## CSV

`src/NtmScheduler.Infrastructure/Csv/`：`EmployeeCsv`、`ScheduleCsv`、`EventsCsv`、`MonthlyShiftCsv`、`CoverageCsv`、`ViolationsCsv`。

一般排班不必用 CSV；網頁「歷史匯入／CSV」頁提供選用批次匯入。
