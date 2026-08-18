# 07. 系統架構與技術決策

> 本文件描述第一版產品的實作架構；檔案與頁面清單以 `11-implementation-plan.md` 為準。

## 1. 總體形態

單一 ASP.NET Core Blazor Web App（Interactive Server）＋單一資料庫＋單一背景求解佇列。
M、T 的 Solver 分開。**不使用**微服務、CQRS、Message Bus 或另一套 JavaScript SPA。

- **.NET 10**（決策 D-08）。
- 資安要求：**只允許 Microsoft 與 Google 官方套件**，不得隨意引入第三方套件。

## 2. 目標專案結構與相依規則

| 專案 | 主要責任 | 相依 |
|---|---|---|
| `NtmcScheduler.Contracts` | Domain、DTO、規則結果、排班服務介面 | 無（**不得參考 OR-Tools 或 EF Core**） |
| `NtmcScheduler.Solvers` | OR-Tools、M／T solver、硬／軟規則、固定 Priority 群組最佳化、候選解 | Google.OrTools |
| `NtmcScheduler.Infrastructure` | EF Core、設定、CSV、背景工作、稽核、匯出；負責呼叫 Solvers | Contracts、Solvers |
| `NtmcScheduler.Web` | Blazor UI、操作服務、進度與結果呈現 | Contracts、Infrastructure |
| `NtmcScheduler.Solvers.Tests` | Solver、CLI 與 Infrastructure 單元／整合測試 | 全部 |

硬性規範：

- 凡介面參數含 `CpModel`、`BoolVar`、`IntVar`、`LinearExpr`，該介面必須位於 Solvers，不得放入 Contracts。
- Blazor 元件不得直接建立 OR-Tools 模型；驗證與求解皆由後端服務執行。

## 3. 主要系統能力（模組責任）

| 模組 | 必須負責的內容 |
|---|---|
| 人員與月班組 | M 所屬站；T 專業、能力（1–5）及每月班別；主檔與月份快照分離 |
| 事件與歷史 | R\*、X、`★` 採用版、上傳歷史、延伸日與跨月狀態 |
| 規則說明 | 班表警示顯示有意義的規則名稱與繁中原因；第一版不提供執行時開關、排序或調參 |
| 排班服務 | 輸入驗證、M/T 選擇、依固定 Priority 群組做字典序最佳化、最多 3 份候選 |
| 班表版本驗證 | 人工修改後由後端驗證服務重算 P0、Coverage、文件既有軟規則與統計 |
| 版本與稽核 | 每次候選成為可編輯版本；唯一採用指標、軟刪除、修改者、時間及前後值 |
| 背景工作 | ScheduleRun 先寫入資料庫，再由 BackgroundService 依序執行；重啟可重建工作 |

## 4. 主要資料實體

至少需要下列實體，且不得遺失本組文件要求的歷史、版本與快照資訊。

| 實體 | 承載資訊 |
|---|---|
| `Employee` | 目前員工主檔：員工編號、姓名、單位（M/T）、M 所屬站、T 專業與 ability、revision token；刪除前內容保存在 AuditLog，既有月份使用獨立快照 |
| `ConfigurationRevision` | 不可變的 56 日區間、國定假日與非常態班型版本；`CurrentConfiguration` 指向目前版 |
| `DemandDraft`／`DemandEmployee`／`DemandAssignment` | 每單位每月一份草稿、月份人員快照、T 月班別、期初額度、R\* 與 X |
| `EmployeeDemandSubmission`／`EmployeeDemandSubmissionAssignment` | 每工作區／月份／員工一份目前填報；任何登入者可代填，AuditLog 保留每次覆蓋 |
| `DemandSubmissionImport` | 記錄 Demand 從填報一次性匯入的時間與操作者；匯入後填報仍可保存但標示晚於截止 |
| `UploadedPreviousSchedule` | 解析後的上月班表 typed JSON，不保存原始上傳檔；僅屬該 Demand，不建立 `ScheduleVersion` |
| `MPerpetualScheduleTemplate` | M 全工作區一份全域 56 日萬年班表；Demand 可另存暫用模板快照 |
| `ScheduleRun` | 排班請求、狀態、完整 typed input JSON（人員、事件、歷史、設定、seed、程式版本）與 SHA-256 hash |
| `ScheduleVersion` | 每次求解候選或匯入結果；各版本皆可人工修改與封存 |
| `ScheduleAssignment` | 班表版本的人 × 日期狀態（含跨站站碼與 X 起訖） |
| `AdoptedSchedule` | 每單位每月最多一份 `★` 採用版本 |
| `AuditLog` | UTC、操作者快照、動作、前後值、SessionId、IP、User-Agent 與 CorrelationId |

## 5. 資料庫

- EF Core。開發環境用 **SQLite**；正式環境使用 **SQL Server**。
- 不使用 provider 專屬查詢；單一 migration 可分別產生 SQLite 與 SQL Server script。
- SQLite 啟動後使用 WAL journal，避免背景求解保存班表時阻塞 UI 讀取；載入完整班表的多集合查詢使用 EF Core split query，避免集合笛卡兒積。

## 6. 登入與授權（第一版）

- 使用 ASP.NET Core Identity 本機帳號；不開放自助註冊、忘記密碼或電子郵件重設。
- Administrator 建立帳號、設定一次性密碼、停用帳號及配置 M/T 工作區編輯權；首次登入必須改密碼。
- 所有已登入者可檢視；只有對應工作區 Editor 或 Administrator 可寫入。共同設定可由任一 Editor 修改。
- 路由、元件與後端 application service 都執行授權檢查，不能只依 UI 隱藏按鈕。
- 互動中的驗證狀態失效時，強制重新載入根頁，由 Cookie middleware 回到登入首頁，不保留失效的互動頁面。
- 第一版不做 MFA；未來可替換為 AD／Entra authentication provider，不改工作區權限及稽核模型。

## 7. 後端固定設定

| 類別 | 內容 |
|---|---|
| 班別 | M／T 早、午、夜的起訖時間 |
| M 營運 | 車站群組、每日班位需求、可外派車站（LB02／LB04／LB09／LB11）、八週萬年班表 hint |
| T 營運 | 月輪轉順序（早→午→夜→早），及必要時的下月班組例外 |
| 休假週期 | 每個 8 週週期的 start、end、requiredR（一般休假，預設 16）、requiredR1（國定假日數） |
| 規則 | 固定群組、違反量與權重寫在 M/T solver 原始碼；M 為 J1 與直接加權合併的 `J4+J5`，T 為 J1–J5 |
| 求解 | CLI 以 `--search workers=N,seconds=N` 調整 worker 數與總時限。M 另可設 `seeds=N`，預設 4 workers、2 seeds、300 秒，並選取第一候選字典序較佳的整批結果；T 固定單 seed 0，預設 8 workers、300 秒。不預留固定候選時間，目標最佳化後才以餘時搜尋。差異門檻固定 5%，最多 3 份候選。M 找不到同分第二份時各目標最多可差 20%；T 候選維持同分 |

## 8. 背景工作與併發

- ScheduleRun 先寫入資料庫（Queued），BackgroundService 依序取出執行，**一次一個** Solver。
- 系統重啟後，Queued／Running 的工作依快照重新開始。
- 同單位同月可保存多份班表，`AdoptedSchedule` 只允許一份 `★`；每次儲存格修改都送後端重新驗證，
  以後端驗證結果為準。紅色違規版本不可標示為 `★`。
