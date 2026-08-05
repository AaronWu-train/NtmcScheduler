# 新北捷人員排班系統（NtmScheduler）

新北捷運公司內部的人員排班系統。輸入人員資料、指定休假（R\*）、公務事件（X）與歷史班表後，
系統以 OR-Tools CP-SAT 依規則產生月班表候選（最多 3 份），使用者挑選一份為 Draft、
可人工修改，通過全部硬規則後發布為 Published。

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

`docs/` 是本專案的**唯一真相來源**，取代原始 PDF 規格書
（`新北捷人員排班系統_完整開發規格書_v6.pdf`）。文件間如有衝突，以 `docs/10-decisions.md` 的最新決策為準。

| 文件 | 內容 |
|---|---|
| [docs/01-scope-and-workflow.md](docs/01-scope-and-workflow.md) | 第一版範圍、班表生命週期、版本規則 |
| [docs/02-glossary.md](docs/02-glossary.md) | 名詞、班表代號、區段／區塊定義、排班區間與時間邊界 |
| [docs/03-data-and-validation.md](docs/03-data-and-validation.md) | 輸入資料、CSV 格式、X 事件規則、歷史資料、INVALID_INPUT 清單 |
| [docs/04-hard-rules.md](docs/04-hard-rules.md) | P0 硬性規則（違反即非法班表）與發布前檢查 |
| [docs/05-soft-rules.md](docs/05-soft-rules.md) | P1–P4 軟規則、違反量定義、預設順序、逐條最佳化 |
| [docs/06-solver-and-output.md](docs/06-solver-and-output.md) | 求解流程、狀態、候選差異、缺班分析、輸出檔格式 |
| [docs/07-architecture.md](docs/07-architecture.md) | 專案結構、相依規則、資料實體、背景工作、固定設定 |
| [docs/08-frontend.md](docs/08-frontend.md) | 前端功能需求、互動寬表班表管理器 |
| [docs/09-acceptance.md](docs/09-acceptance.md) | 最低驗收案例 |
| [docs/10-decisions.md](docs/10-decisions.md) | 決策紀錄（規格釐清的問答結果與裁定） |

## Agent 工作守則

1. **遇到文件未定義的業務情況，回報規格缺口，不得自行補上業務假設。** 這是本專案的最高原則。
2. **Rule ID 一旦定義即固定不變**（例如 `GEN-H-02`、`T-S-ABILITY`），程式、資料庫、UI、匯出檔都必須使用相同 ID。
3. `NtmScheduler.Core` **不得參考 OR-Tools 或 EF Core**。凡介面參數含
   `CpModel`、`BoolVar`、`IntVar`、`LinearExpr` 者，該介面必須位於 `NtmScheduler.Solvers`。
4. Blazor 元件不得直接建立 OR-Tools 模型；驗證與求解一律由後端服務執行。
5. 修改任何規則行為前，先更新對應的 `docs/` 文件，並在 `docs/10-decisions.md` 追加一筆決策紀錄。
6. P0 規則不可關閉；P1 固定最高順位；P2–P4 可由管理者開關與排序，程式不得寫死其順序。
7. 所有時間以台北時間（UTC+8）處理；夜班歸屬其**開始日期**。
