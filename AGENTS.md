# 新北捷人員排班系統（NtmScheduler）

新北捷運公司內部的人員排班系統。輸入人員資料、指定休假（R\*）、每人本月 R休數、公務事件（X）與歷史班表後，
系統以 OR-Tools CP-SAT 依規則產生月班表候選（最多 3 份），使用者挑選一份成為「目前班表」、
可人工修改並自動驗證；可選建立快照，無審核／發布狀態機。

第一版只處理兩個單位：**站務 M**（12 個車站）與**檢測 T**（月班組輪值）。

## 技術棧（已定案）

| 項目 | 決定 |
|---|---|
| 框架 | ASP.NET Core Blazor Web App（Interactive Server），**.NET 10** |
| 求解 | Google OR-Tools CP-SAT，M 與 T 分開建模 |
| 資料庫 | EF Core。開發用 SQLite；正式環境為 PostgreSQL 或 SQL Server（尚未定案），因此**禁止使用 provider 專屬語法** |
| 套件 | 基於資安要求，**只允許 Microsoft 與 Google 官方套件** |
| 登入 | 第一版**不做登入**；保留之後接公司 AD 的擴充點（見 `docs/07-architecture.md`） |
| 語言 | UI 文字一律繁體中文；程式識別字、註解使用英文 |

## 文件索引

一般產品規格以 `docs/` Markdown 與決策紀錄為準；solver 數學公式另以單一報告
[`tex/main2.tex`](tex/main2.tex) 為真相來源（決策 D-21）。原始規格書
[docs/新北捷人員排班系統_完整開發規格書_v6.pdf](docs/新北捷人員排班系統_完整開發規格書_v6.pdf)
應一併參考；若 PDF 與 Markdown／決策衝突，**以 Markdown 與決策為準**。

| 文件 | 內容 |
|---|---|
| [docs/01-scope-and-workflow.md](docs/01-scope-and-workflow.md) | 第一版範圍、班表生命週期、版本規則 |
| [docs/02-glossary.md](docs/02-glossary.md) | 名詞、班表代號、區段／區塊定義、排班區間與時間邊界 |
| [docs/03-data-and-validation.md](docs/03-data-and-validation.md) | 輸入資料、CSV 格式、X 事件規則、歷史資料、INVALID_INPUT 清單 |
| [docs/04-hard-rules.md](docs/04-hard-rules.md) | P0 硬性規則（違反即非法班表）與發布前檢查 |
| [docs/05-soft-rules.md](docs/05-soft-rules.md) | Priority 1–5 固定群組、違反量定義與權重 |
| [docs/06-solver-and-output.md](docs/06-solver-and-output.md) | 求解流程、狀態、候選差異、缺班分析、輸出檔格式 |
| [docs/07-architecture.md](docs/07-architecture.md) | 專案結構、相依規則、資料實體、背景工作、固定設定 |
| [docs/08-frontend.md](docs/08-frontend.md) | 前端功能需求、互動寬表班表管理器 |
| [docs/09-acceptance.md](docs/09-acceptance.md) | 最低驗收案例 |
| [docs/10-decisions.md](docs/10-decisions.md) | 決策紀錄（規格釐清的問答結果與裁定） |
| [docs/11-implementation-plan.md](docs/11-implementation-plan.md) | 實作架構、CP-SAT 演算法、頁面／服務／里程碑 |
| [docs/新北捷人員排班系統_完整開發規格書_v6.pdf](docs/新北捷人員排班系統_完整開發規格書_v6.pdf) | 原始規格書 v6（交叉比對用） |

## Agent 工作守則

1. **遇到文件未定義的業務情況，回報規格缺口，不得自行補上業務假設。** 這是本專案的最高原則。
2. Solver 的短 Rule ID 只存在於文件供交叉查閱；程式以有意義的英文函數與違反量名稱對應公式，不保存 Rule ID（決策 D-21）。
3. `NtmScheduler.Core` **不得參考 OR-Tools 或 EF Core**。凡介面參數含
   `CpModel`、`BoolVar`、`IntVar`、`LinearExpr` 者，該介面必須位於 `NtmScheduler.Solvers`。
4. Blazor 元件不得直接建立 OR-Tools 模型；驗證與求解一律由後端服務執行。
5. 修改任何規則行為前，先更新對應的 `docs/` 文件，並在 `docs/10-decisions.md` 追加一筆決策紀錄。
6. Solver 硬限制不可關閉；軟限制依 `tex/main2.tex` 的固定群組做字典序最佳化，群組與權重直接寫在 M/T solver 原始碼（決策 D-21）；M 使用 J1 與直接加權合併的 `J4+J5`，T 使用 J1–J5。
7. 所有時間以台北時間（UTC+8）處理；夜班歸屬其**開始日期**。
8. `R休` 只可排在目標月的 `R*`，每人輸入數量為上限；未使用數量以權重 1 納入 J1，既有指定休假違反量權重改為 3。它與 R1 同樣屬實際休假但不計入 R/R1 月統計或 56 日額度，也不重置連續七日的一般 R 規則。M 的休假、跨站支援與早／午／夜班數公平性一律在所屬三站群組內比較。

## 實作與驗證備忘

- M/T solver 維持分離、明白的 source-as-spec partial 檔；可接受少量重複，不新增會遮蔽公式的 catalog、definition、encoder、Rule ID map 或規則 DI。
- 修改 CSV 或範例 fixture 前先看 `git status`、staged 與 unstaged diff；已確認的 staged 格式優先。跨月資料應核對本月 `OpeningUsage` 與上月 `ClosingUsage`，不得靠放寬驗證掩蓋 fixture 錯誤，並保留原始檔案編碼。
- 建置與測試使用 `NtmScheduler.slnx`。Solver 測試刻意不平行執行，完整 M/T 案例可能需一分鐘以上；不得只為縮短測試而弱化規則、斷言或求解時限。
- sandbox 若出現 `SocketException (13): Permission denied` 或 named-pipe 錯誤，先在允許本機 IPC 的環境重跑；此錯誤本身不是程式失敗的結論。
- `TimeLimit` 與 `Infeasible` 必須分開回報；`TimeLimit` 可帶合法 incumbent，而目前 `ObjectiveScore` 不記錄各優先組是否已證明最佳，詳見 `docs/06-solver-and-output.md`。
