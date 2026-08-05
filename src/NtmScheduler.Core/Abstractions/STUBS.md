# Core Abstractions 狀態（已非 stub）

本目錄為服務介面與 DTO。實作已接上：

| 介面 | 實作 |
|---|---|
| `ISolveService` | `NtmScheduler.Solvers`（`LexicographicSolveEngine`） |
| `IScheduleRunService`／`ICandidateService`／`IDraftService`／`IPublishService`／`IShortageAnalysisService`／`IPreparationService` | `NtmScheduler.Infrastructure/Services` |
| `IEmployeeService`／`IEventService`／`IMonthlyShiftService`／`IScheduleCycleService`／`IRuleSettingService`／`IHistoryImportService`／`IExportService`／`IAuditService` | 同上 |

DTO 見 `Abstractions/Dtos/`。`ValidationError` 位於 `Core/Validation`。
DI：`AddNtmInfrastructure`（含 `AddSolvers` 與 `ScheduleRunWorker`）。

已知可再加厚：Draft 編輯時休假統計／P0 選項的完整即時計算、Playwright E2E。
