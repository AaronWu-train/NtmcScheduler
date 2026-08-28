# NtmcScheduler

新北捷運人員排班系統。系統依人員資料、指定休假（R*）、公務事件（X）與歷史班表，使用 Google OR-Tools CP-SAT 為三鶯與環狀的站務（M／YM）及檢修（T／YT）產生月班表候選。

主要功能包括：

- Blazor Web 操作介面與 ASP.NET Core Identity 權限管理
- 站務、檢修獨立求解與班表自動驗證
- CSV 匯入、匯出與命令列求解
- 同月份多版本班表保存與採用版管理
- SQLite 開發資料庫與 SQL Server 正式環境

## 快速開始

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。開發環境預設使用 SQLite。

```bash
dotnet tool restore
dotnet run --project src/NtmcScheduler.Web -- --init-admin admin
dotnet run --project src/NtmcScheduler.Web
```

`--init-admin` 會在終端機讀取首位管理者的一次性密碼；首次登入後必須修改密碼。

## 執行 CLI 範例

專案提供可直接求解的 M、T 範例：

```bash
# 站務 M
cd examples/m-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli

# 檢修 T
cd examples/t-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli
```

互動輸入與預期輸出請參閱 [M 範例](examples/m-2026-09/README.md)與 [T 範例](examples/t-2026-09/README.md)；CSV 格式與驗證規則以[資料格式與驗證](docs/03-data-and-validation.md)為準。

## 建置與測試

```bash
dotnet build NtmcScheduler.slnx
dotnet test NtmcScheduler.slnx
```

完整 Solver 測試不平行執行，可能需要一分鐘以上。

## 專案結構

| 路徑 | 內容 |
|---|---|
| `src/NtmcScheduler.Web` | Blazor Web、Identity 與 HTTP 入口 |
| `src/NtmcScheduler.Cli` | 命令列求解工具 |
| `src/NtmcScheduler.Contracts` | 共用資料合約 |
| `src/NtmcScheduler.Infrastructure` | EF Core、服務與 CSV 邊界 |
| `src/NtmcScheduler.Solvers` | M、T Solver 與規則實作 |
| `tests/NtmcScheduler.Solvers.Tests` | 自動化測試 |
| `examples` | M、T 與 Web 範例資料 |
| `docs` | 產品、規則、架構、驗收與部署文件 |

## 文件

| 主題 | 文件 |
|---|---|
| 範圍、流程與名詞 | [範圍與營運流程](docs/01-scope-and-workflow.md)、[名詞與時間邊界](docs/02-glossary.md) |
| 輸入資料與 CSV | [資料格式與驗證](docs/03-data-and-validation.md) |
| 排班規則與求解結果 | [硬限制](docs/04-hard-rules.md)、[軟規則](docs/05-soft-rules.md)、[求解與輸出](docs/06-solver-and-output.md) |
| 系統設計與操作介面 | [系統架構](docs/07-architecture.md)、[前端功能](docs/08-frontend.md) |
| 驗收 | [系統驗收](docs/09-acceptance.md) |
| 開發 | [開發指南與實作結構](docs/11-implementation-plan.md) |
| Solver 數學模型 | [數學模型報告](docs/tex/main2.pdf) |
| 正式環境 | [Ubuntu 24.04＋SQL Server 部署指南](docs/12-deployment.md) |

業務規則與資料格式以 `docs/` 為準；修改規則前請先閱讀 [AGENTS.md](AGENTS.md)。

## 更新部署

升級前先依[部署指南](docs/12-deployment.md#11-升級既有部署)備份資料庫，再於正式機的專案目錄執行：

```bash
git pull
bash rebuild_and_deploy.sh
```
