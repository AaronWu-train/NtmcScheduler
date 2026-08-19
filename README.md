# NtmcScheduler

新北捷運人員排班系統。依人員資料、指定休假（R*）、公務事件（X）與歷史班表，以 OR-Tools CP-SAT 產生最多三份月班表候選。

目前 repository 已包含站務（M）、檢修（T）的 Solver、共用 CSV adapter、Blazor Interactive Server、EF Core／Identity、背景求解佇列與多版本班表管理。

## 環境需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 快速開始：Web

開發環境預設使用 SQLite。先建立資料庫與首位管理者，再啟動網站：

```bash
dotnet tool restore
dotnet run --project src/NtmcScheduler.Web -- --init-admin admin
dotnet run --project src/NtmcScheduler.Web
```

第一個命令只還原 repository-local Microsoft `dotnet-ef`；`--init-admin` 會在終端機安全讀取一次性密碼，首次登入必須修改。正式環境設定 `DatabaseProvider=SqlServer`、`ConnectionStrings__Default`、持久化 `DataProtection__KeyPath` 與 `DataProtection__CertificatePath`，secret 不寫入設定檔。

## 快速開始：CLI

CLI 不使用 Web 或資料庫，讀取上月班表、本月需求、八週區間與非常態班型 CSV 後，直接呼叫 M 或 T Solver。需先安裝 .NET 10 SDK。

日期文字一律使用零補齊的 ISO 形式：完整日期 `yyyy-MM-dd`、月份 `yyyy-MM`、只有月日時 `MM-dd`；不接受斜線或省略前導零。

專案附有可直接執行的 M 與 T 範例：

```bash
# 站務 M
cd examples/m-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli

# 或檢修 T
cd examples/t-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli
```

兩個範例依序輸入：

```text
2026-09
previous.csv
demand.csv
rest-intervals.csv
non-standard-shifts.csv
```

前五個問題分別是：

1. 目標月份，格式為 `yyyy-MM`。
2. 上月班表 CSV；留空代表沒有歷史，只適用於本月到職的新進人員。
3. 本月需求 CSV；留空使用目前目錄的 `demand.csv`。
4. 八週區間 CSV；留空使用目前目錄的 `rest-intervals.csv`。
5. 非常態班型 CSV；留空使用目前目錄的 `non-standard-shifts.csv`。

若本月需求判定為 M，CLI 會再詢問八週萬年班表 CSV；留空代表本次不使用 hint。

CLI 會依本月需求的「能力」與「T月班別」欄自動判斷單位：兩欄全部留空為 M，兩欄全部填寫為 T，不可混用。求解結果會顯示狀態、候選數量與各 Priority 分數，並在目前目錄輸出 `candidate-N.csv`；M 有外派時另輸出同編號的 `candidate-N-external.csv`。若編號已有主檔或外派檔，整批改用下一段連續可用編號，不詢問也不覆寫。`Ctrl+C` 可取消求解。

Exit code：有候選為 `0`；輸入錯誤、無解或沒有候選為 `1`；取消為 `130`。

### 月班表 CSV：`previous.csv` 與 `demand.csv`

兩個檔案使用相同且順序固定的新 46 欄表頭；沒有末欄的舊 45 欄檔仍可讀取：

```text
ID,姓名,所屬,到職日期,能力,T月班別,月初區間累計R,月初區間累計R1,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,當月R,當月R1,當月指定R休,月底區間累計R,月底區間累計R1,本月班數,萬年班表
```

| 欄位 | 寫法 |
|---|---|
| ID | 員工唯一識別碼，不可重複 |
| 姓名 | 不可留空 |
| 所屬 | M 填 `LB01`–`LB12` 車站；T 填專業分組 |
| 到職日期 | `yyyy-MM-dd`；既有人員可留空，本月沒有歷史的新進人員必須填本月日期 |
| 能力 | M 留空；T 填 `1`–`5` |
| T月班別 | M 留空；T 填 `早`、`午` 或 `夜` |
| 月初區間累計R／R1 | 本月 1 日開始前，在月初所屬八週區間內的累計數量；兩欄必須一起填或一起留空 |
| 1–31 | 每日需求或已排結果，格式見下表；不存在的日期與到職日前必須留空 |
| 當月R／R1 | 由 1–31 日格自動計算並核對；`previous.csv` 必填，`demand.csv` 必須留空 |
| 當月指定R休 | `demand.csv` 填可使用上限，空白視為 0；歷史與候選填由日格重算的實際 R休數 |
| 月底區間累計R／R1 | 截至本月最後一日，在月底所屬八週區間內的累計數量；`previous.csv` 必填，`demand.csv` 必須留空 |
| 本月班數 | `previous.csv` 必填；`demand.csv` 必須留空 |
| 萬年班表 | M 可填 56 日模板代號；本月留白承接上月同員工代號，T 留空 |

每日欄位可使用：

| 值 | 意義 |
|---|---|
| 空白 | 本月需求中交給 Solver 決定 |
| `R`／`R1` | 已確定的一般休假／國定假日休假 |
| `R休` | 已確定的歷史 R休；本月需求不可直接填寫 |
| `R*` | 指定休假，尚待 Solver 決定計入 R、R1 或 R休 |
| `R*[R]`／`R*[R1]`／`R*[R休]` | 指定休假，且已確定實際休假類型 |
| `X[08:30-17:30]` | 公務事件，時間必須是 `HH:mm-HH:mm`；結束時間不晚於開始時間時視為隔日結束，最長 24 小時 |
| 非常態班型或代碼 | 依 `non-standard-shifts.csv` 查表後轉成 X；例如 `日一` 或 `0837` |
| `LB01早`／`1早` | M 固定工作；完整格式為 `LB01`–`LB12` 加班別，也接受站號 `1`–`12` 的簡寫 |
| `LB01小`／`1小` | M 小班（午班）；輸入也相容 `LB01午`／`1午` |
| `早`／`午`／`夜` | T 固定工作；可不同於該列「T月班別」，但會計入 `NonMonthlyShift` 軟規則 |

`previous.csv` 必須是目標月份的前一個月；每位仍在職員工的每一天都要有已確定結果，不能有空白或未解決的 `R*`，並須提供月底 R／R1 累積與正常班次數。`demand.csv` 是本月人員的唯一來源：上月有但本月沒有的人不會排入本月。

M 簡寫會在讀入時正規化：例如 `1早` 解讀為 `LB01早`、`12小` 或 `12午` 解讀為 `LB12小`；M 月班表與萬年班表 CSV 下載一律以站號簡寫（如 `1早`、`12小`）輸出正常班，午班以 `小` 表示。T 午班仍輸出 `午`。

### M 八週萬年班表 CSV（可選）

```text
萬年班表,1,2,...,56
LB01-1,3午,3午,R,...
```

每列包含唯一模板代號與完整 56 日，只接受 `R` 或 M 正常站班。模板第 1 日對應每個 16R 八週區間首日；員工由月班表末欄指定模板。模板只以 OR-Tools `AddHint` 引導初始可行解，固定格、R*、硬限制與各 Priority 仍優先，最終候選不保證與模板相同。

### 非常態班型 CSV：`non-standard-shifts.csv`

```csv
班型,時間,代碼
早一,06:30~14:30,0635
,08:00~16:00,0805
日一,08:30~17:30,0837
夜一,22:30~06:30,2235
```

代碼與時間必填，班型可留空；所有非空白班型與代碼必須互不重複。時間使用 `HH:mm~HH:mm`，跨午夜班型歸開始日。日格中的 `早`、`午`、`夜`仍是 T 正常班，同列代碼則解析成 X。候選輸出一律正規化為 `X[HH:mm-HH:mm]`。

### 八週區間 CSV：`rest-intervals.csv`

表頭固定為三欄：

```csv
區間開始日期,區間結束日期,國定假日日期
2026-07-20,2026-09-13,2026-08-14
2026-09-14,2026-11-08,2026-09-18;2026-10-12
```

- 每列區間含首含尾必須剛好 56 日；相鄰區間必須連續、不重疊且沒有缺口。
- Solver 使用到的每一天都必須剛好落在一個區間。
- 國定假日格式為 `yyyy-MM-dd`，多個日期以半形分號 `;` 分隔，沒有則留空。
- 國定假日必須位於該列區間內且為週一至週五；六、日不必填入。

所有 CSV 欄位與順序必須完全符合表頭。檔案使用 UTF-8，可含 BOM；含逗號、雙引號或換行的欄位須使用標準 CSV 雙引號跳脫。本版不讀取 `.xlsx`。完整範例見 [`examples/m-2026-09`](examples/m-2026-09) 與 [`examples/t-2026-09`](examples/t-2026-09)。

## 建置與測試

```bash
dotnet build NtmcScheduler.slnx
dotnet test NtmcScheduler.slnx
```

## 專案結構（去哪找什麼）

| 想找什麼 | 路徑 |
|---|---|
| CLI 進入點 | `src/NtmcScheduler.Cli/Program.cs` |
| Web 入口與資安 middleware | `src/NtmcScheduler.Web/Program.cs` |
| Blazor 頁面 | `src/NtmcScheduler.Web/Components/Pages/` |
| EF 實體與 migration | `src/NtmcScheduler.Infrastructure/Data/` |
| Application services | `src/NtmcScheduler.Infrastructure/Services/` |
| CSV 邊界 | `src/NtmcScheduler.Infrastructure/Csv/ScheduleCsv.cs` |
| M 求解流程 | `src/NtmcScheduler.Solvers/MSolver.cs` |
| M 硬／軟規則 | `src/NtmcScheduler.Solvers/MSolver.HardRules.cs`、`MSolver.SoftRules.cs` |
| T 求解流程 | `src/NtmcScheduler.Solvers/TSolver.cs` |
| T 硬／軟規則 | `src/NtmcScheduler.Solvers/TSolver.HardRules.cs`、`TSolver.SoftRules.cs` |
| 共用 contracts | `src/NtmcScheduler.Solvers/SolverContracts.cs` |
| Solver 與 CLI 導覽 | `docs/11-implementation-plan.md` |
| 軟規則總表 | `docs/05-soft-rules.md` |

## 技術棧

- .NET 10 Blazor Interactive Server＋Contracts／Infrastructure／Solver／CLI
- Google OR-Tools CP-SAT（M／T 分開建模，依固定 Priority 群組做字典序最佳化）
- ASP.NET Core Identity、EF Core（SQLite 開發、SQL Server 正式）；詳見 `docs/07-architecture.md`

## 文件

第一個穩定版為 **v1.0.0**（2026-08-18，git tag `v1.0.0`）；目前穩定版為 **v1.0.2**（git tag `v1.0.2`）。產品範圍仍是「第一版」（站務 M、檢修 T）。正式環境部署步驟見 [`docs/12-deployment.md`](docs/12-deployment.md)；尚待完成的部署驗收與上線前密碼政策見 `docs/11-implementation-plan.md`。

閱讀順序：`01` 範圍 → `02` 名詞 → `03` 資料 → `04`–`06` 規則與求解 → `07`–`08` 架構與畫面 → `09` 驗收 → `10` 決策（由舊到新）→ `11` 實作檔案。業務規則與資料格式見 [`docs/`](docs/)。Agent 守則見 [`AGENTS.md`](AGENTS.md)。
