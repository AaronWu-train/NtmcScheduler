# 07. 系統架構與技術決策

> 本文件描述第一版產品的實作架構；檔案與頁面清單以 `11-implementation-plan.md` 為準。

## 1. 總體形態

單一 ASP.NET Core Blazor Web App（Interactive Server）＋單一資料庫＋單一背景求解佇列。
站務與 T 的 Solver 分開；三鶯 M 與環狀 YM 共用同一個 `MSolver`。**不使用**微服務、CQRS、Message Bus 或另一套 JavaScript SPA。

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
| 人員與月班組 | M／YM 所屬站；T 專業、能力（1–5）及每月班別；主檔與月份快照分離 |
| 事件與歷史 | R\*、X、`★` 採用版、上傳歷史、延伸日與跨月狀態 |
| 規則說明 | 班表警示顯示有意義的規則名稱與繁中原因；第一版不提供執行時開關、排序或調參 |
| 排班服務 | 輸入驗證、M／YM 共用 `MSolver`、T 使用 `TSolver`，依固定 Priority 群組做字典序最佳化、最多 3 份候選；若 M 與 YM 後續規則不同再另案拆分或加入明確分支 |
| 班表版本驗證 | 人工修改後由後端驗證服務重算 P0、Coverage、文件既有軟規則與統計 |
| 版本與稽核 | 每次候選成為可編輯版本；唯一採用指標、軟刪除、修改者、時間及前後值 |
| 背景工作 | ScheduleRun 先寫入資料庫，再由 BackgroundService 依序執行；重啟可重建工作 |

## 4. 主要資料實體

至少需要下列實體，且不得遺失本組文件要求的歷史、版本與快照資訊。

| 實體 | 承載資訊 |
|---|---|
| `Employee` | 目前員工主檔：員工編號、姓名、工作區（M/T/YM/YT）、M/YM 所屬站、T/YT 專業與 ability、revision token；刪除前內容保存在 AuditLog，既有月份使用獨立快照 |
| `ConfigurationRevision` | 不可變的 56 日區間、國定假日、非常態班型與 M/T/YM/YT 各三班起訖時間版本；`CurrentConfiguration` 指向目前版 |
| `DemandDraft`／`DemandEmployee`／`DemandAssignment` | 每單位每月一份草稿、月份人員快照、R／R1 軟目標；M/YM 另存各自固定站點的群組、班位上下限與外援等級快照；並含 T 月班別、期初額度、R\* 與 X |
| `EmployeeDemandSubmission`／`EmployeeDemandSubmissionAssignment` | 每工作區／月份／員工一份目前填報；任何登入者可代填，AuditLog 保留每次覆蓋 |
| `DemandSubmissionImport` | 記錄 Demand 最近一次從填報匯入的時間與操作者；重複匯入時更新此紀錄；填報頁以之判斷晚於截止的填報 |
| `UploadedPreviousSchedule` | 解析後的上月班表 typed JSON，不保存原始上傳檔；僅屬該 Demand，不建立 `ScheduleVersion` |
| `MPerpetualScheduleTemplate` | M 與 YM 各自一份全域 56 日萬年班表；Demand 可另存暫用模板快照，兩個工作區以 `Workspace` 隔離 |
| `ScheduleRun` | 排班請求、狀態、完整 typed input JSON（人員、事件、歷史、設定、seed、程式版本）與 SHA-256 hash |
| `ScheduleVersion` | 每次求解候選或匯入結果；各版本皆可人工修改與封存 |
| `ScheduleAssignment` | 班表版本的人 × 日期狀態（含跨站站碼與 X 起訖） |
| `AdoptedSchedule` | 每單位每月最多一份 `★` 採用版本 |
| `AuditLog` | UTC、操作者快照、動作、前後值、SessionId、IP、User-Agent 與 CorrelationId |

## 5. 資料庫

- EF Core。開發環境用 **SQLite**；正式環境使用 **SQL Server**。
- 不使用 provider 專屬查詢；SQLite 與 SQL Server 各自使用獨立 migration project、歷史與 model snapshot。每次 model 變更必須為兩個 provider 分別新增 migration。
- SQLite 啟動後使用 WAL journal，避免背景求解保存班表時阻塞 UI 讀取；載入完整班表的多集合查詢使用 EF Core split query，避免集合笛卡兒積。
- Blazor Interactive Server 的 circuit scope 存活期間長，且同一 scope 內多個元件可以並行查詢。Application service 與 `CurrentActorService` 透過 `IDbContextFactory<NtmcDbContext>` 為每次資料庫操作建立並釋放獨立 context。啟動 migration、HTTP 下載端點、登入／改密碼靜態表單，以及背景工作自行建立的短生命週期 scope 仍可直接解析 scoped `NtmcDbContext`。帳號管理因 Identity `UserManager` 必須與 `NtmcDbContext` 共用同一 context 與交易，改為每個操作建立短生命週期 scope。

## 6. 登入與授權（第一版）

- 使用 ASP.NET Core Identity 本機帳號；不開放自助註冊、忘記密碼或電子郵件重設。
- Administrator 建立帳號、設定一次性密碼、停用帳號及配置 M/T/YM/YT 工作區編輯權；四者不互相繼承，首次登入或重設後必須改密碼。已登入者可從頁首隨時自行修改密碼，須驗證目前密碼。
- 所有已登入者可檢視；只有對應工作區 Editor 或 Administrator 可寫入。共同設定可由任一 Editor 修改。
- 路由、元件與後端 application service 都執行授權檢查，不能只依 UI 隱藏按鈕。
- 互動中的驗證狀態失效時，強制重新載入根頁，由 Cookie middleware 回到登入首頁，不保留失效的互動頁面。
- 第一版不做 MFA；未來可替換為 AD／Entra authentication provider，不改工作區權限及稽核模型。

## 7. 後端固定設定

| 類別 | 內容 |
|---|---|
| 班別 | M／T／YM 早、午、夜起訖時間（可透過工作區設定頁分別維護，儲存於 `ConfigurationRevision`；YM 初值與 M 原始預設相同，但之後互不連動） |
| M 營運 | 每月車站／群組、每日班位人數上下限、外援等級、八週萬年班表 hint |
| T 營運 | 月輪轉順序（早→午→夜→早），及必要時的下月班組例外 |
| 休假週期 | 每個 8 週週期的 start、end、requiredR（一般休假，預設 16）、requiredR1（國定假日數） |
| 規則 | 硬規則、Priority 與違反量公式寫在站務/T solver；M 與 YM 共用 `MSolver`，每次求解保存目前啟用軟規則的權重快照，M/YM 為 Priority 1、2（公式仍為 J1 與直接加權合併的 `J4+J5`），T 為 J1–J5 |
| 求解 | CLI 以 `--search workers=N,seconds=N` 調整 worker 數與總時限。M 另可設 `seeds=N`，seed 依序執行、各自計時，預設 8 workers、2 seeds、300 秒，並選取第一候選字典序較佳的整批結果；T 固定單 seed 0，預設 8 workers、300 秒。Web 求解參數上限為 600 秒／seed、8 workers、4 seeds。不預留固定候選時間，目標最佳化後才以餘時搜尋。差異門檻固定 5%，最多 3 份候選。M 找不到同分第二份時各目標最多可差 20%；T 候選維持同分 |

## 8. 背景工作與併發

- ScheduleRun 先寫入資料庫（Queued），BackgroundService 依序取出執行，**一次一個** Solver。
- 系統重啟後，Queued／Running 的工作依快照重新開始。
- Queued 與 Running 的工作可由該工作區 Editor 或 Administrator 手動取消，成為終態 `Cancelled`。
  取消訊號是同一程序內的 cancellation token，由背景工作在求解實際停止後寫入終態；已取消的工作
  不保留任何候選，也不產生班表版本。重啟會讓未完成的工作重新排隊，不沿用先前的取消要求。
- 同單位同月可保存多份班表，`AdoptedSchedule` 只允許一份 `★`；每次儲存格修改都送後端重新驗證，
  以後端驗證結果為準。紅色違規版本不可標示為 `★`。
