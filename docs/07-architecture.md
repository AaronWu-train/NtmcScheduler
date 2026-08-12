# 07. 系統架構與技術決策

> 本文件描述第一版產品的目標架構。目前 repository 只完成 Solver 與 CLI；現有程式路徑以 `11-implementation-plan.md` 為準。

## 1. 總體形態

單一 ASP.NET Core Blazor Web App（Interactive Server）＋單一資料庫＋單一背景求解佇列。
M、T 的 Solver 分開。**不使用**微服務、CQRS、Message Bus 或另一套 JavaScript SPA。

- **.NET 10**（決策 D-08）。
- 資安要求：**只允許 Microsoft 與 Google 官方套件**，不得隨意引入第三方套件。

## 2. 目標專案結構與相依規則

| 專案 | 主要責任 | 相依 |
|---|---|---|
| `NtmScheduler.Core` | Domain、DTO、規則結果、排班服務介面 | 無（**不得參考 OR-Tools 或 EF Core**） |
| `NtmScheduler.Solvers` | OR-Tools、M／T solver、硬／軟規則、固定 Priority 群組最佳化、候選解、缺班分析 | Core、Google.OrTools |
| `NtmScheduler.Infrastructure` | EF Core、設定、CSV、背景工作、稽核、匯出；負責呼叫 Solvers | Core、Solvers |
| `NtmScheduler.Web` | Blazor UI、操作服務、進度與結果呈現 | Core、Infrastructure |
| `NtmScheduler.Tests` | 單元、整合與端對端測試 | 全部 |

硬性規範：

- 凡介面參數含 `CpModel`、`BoolVar`、`IntVar`、`LinearExpr`，該介面必須位於 Solvers，不得放入 Core。
- Blazor 元件不得直接建立 OR-Tools 模型；驗證與求解皆由後端服務執行。

## 3. 主要系統能力（模組責任）

| 模組 | 必須負責的內容 |
|---|---|
| 人員與月班組 | M 所屬站；T 專業、能力（1–5）及每月班別 |
| 事件與歷史 | R\*、X、目前班表／歷史快照、延伸日與跨月狀態 |
| 規則說明 | 顯示固定群組、權重及白話說明；M 為 J1 與直接加權合併的 `J4+J5`，T 為 J1–J5；第一版不提供執行時開關、排序或調參 |
| 排班服務 | 輸入驗證、M/T 選擇、依固定 Priority 群組做字典序最佳化、最多 3 份候選 |
| 目前班表驗證 | 人工修改後由後端驗證服務重算 P0、Coverage、軟規則指標 |
| 快照與稽核 | Candidate→目前班表、可選快照、修改者、時間及前後值 |
| 背景工作 | ScheduleRun 先寫入資料庫，再由 BackgroundService 依序執行；重啟可重建工作 |

## 4. 主要資料實體

至少需要下列實體。詳細欄位與資料庫拆表可在程式設計階段決定，
但**不得遺失**本組文件要求的歷史、版本與快照資訊。

| 實體 | 承載資訊 |
|---|---|
| `Employee` | 員工編號、姓名、單位（M/T）、M 的 homeStation、T 的 specialty 與 ability |
| `EmployeeMonthlyShift` | T 每人每月班組（早／午／夜） |
| `FixedEvent` | R\*（人＋日期）與 X（人＋起訖時間＋說明） |
| `NonStandardShift` | 前端可維護的非常態班型名稱、唯一代碼與起訖時間；月班表名稱／代碼讀入後解析為 X |
| `ScheduleCycle` | 8 週週期 start、end、requiredR（一般休假，預設 16）、requiredR1（國定假日數） |
| `ScheduleRun` / `Snapshot` | 排班請求、狀態、輸入快照（人員、事件、歷史截止點、固定設定、seed、程式版本） |
| `CandidateSolution` | 求解候選與其品質指標 |
| `Assignment` | 人 × 日期的狀態（含跨站站碼）；Schedule／Snapshot／Candidate 共用結構 |
| `MonthSchedule` | 每單位每月一份目前可編輯班表 |
| `ScheduleSnapshot` | 歷史匯入或手動快照；舊版唯讀 |
| `AuditLog` | 操作者、時間、動作、前後值 |

## 5. 資料庫

- EF Core。開發環境用 **SQLite**；正式環境將於日後在 PostgreSQL 與 SQL Server 之間選定（決策 D-07）。
- 因此：不使用 provider 專屬 SQL／函式；migration 保持中立；用整合測試確保可切換。

## 6. 登入與授權（第一版）

- **第一版不做登入**；所有頁面與功能對可連線者開放（決策 D-04，內網環境）。
- 但必須**預留擴充點**，之後可能接公司 AD：
  - 所有操作經由後端服務層（介面）執行，未來可在該層加授權檢查。
  - Web 專案保留 ASP.NET Core authentication/authorization middleware 的掛載位置（暫不啟用）。
  - `AuditLog` 的「操作者」欄位第一版由介面上的「目前操作者」輸入欄帶入
    （純文字，供追溯）；接 AD 後改為登入者身分。

## 7. 後端固定設定

| 類別 | 內容 |
|---|---|
| 班別 | M／T 早、午、夜的起訖時間 |
| M 營運 | 車站群組、每日班位需求、可外派車站（LB02／LB04／LB09／LB11）、八週萬年班表 hint |
| T 營運 | 月輪轉順序（早→午→夜→早），及必要時的下月班組例外 |
| 休假週期 | 每個 8 週週期的 start、end、requiredR（一般休假，預設 16）、requiredR1（國定假日數） |
| 規則 | 固定群組、違反量與權重寫在 M/T solver 原始碼；M 為 J1 與合併的 `1000×J4+J5`，T 為 J1–J5 |
| 求解 | 總時限（預設 5 分鐘）、seed、差異門檻（預設 10%）；候選目標數固定 3 |

## 8. 背景工作與併發

- ScheduleRun 先寫入資料庫（Queued），BackgroundService 依序取出執行，**一次一個** Solver。
- 系統重啟後，Queued／Running 的工作依快照重新開始。
- 目前班表：同單位同月同時只有一份；每次儲存格修改都送後端重新驗證，
  以後端驗證結果為準（決策 D-14f、D-20）。
