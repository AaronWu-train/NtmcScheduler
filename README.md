# NtmScheduler

新北捷運人員排班系統。依人員資料、指定休假（R*）、公務事件（X）與歷史班表，以 OR-Tools CP-SAT 產生最多三份月班表候選；使用者選一份成為「目前班表」，可在寬表中隨時修改，每次修改自動保存並重新檢查規則。

目前支援站務（M）與檢測（T）兩個單位。

## 環境需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 快速開始

```bash
dotnet run --project src/NtmScheduler.Web --launch-profile http
```

瀏覽器開啟 http://localhost:5109。

開發環境使用 SQLite，首次啟動會自動建立／遷移 `ntm.db`。

### 載入範例並完成一次排班

1. 開啟首頁，點「**載入範例資料**」（會寫入 12 站站務人員、約 30 名檢測人員、R*／X、歷史班表、8 週週期）。
2. 確認目標月為 `2026-08`，準備狀態顯示可建立 Run。
3. 前往「求解 Run」→ 選單位（M 或 T）→「建立 Run」。
4. 等待求解完成 →「候選比較」→「選為目前班表」。
5. 在寬表點格子修改；修改會自動保存並重新驗證。

一般流程**不需要**上傳 CSV。CSV 僅作選用的批次工具（人員、寬表匯入／匯出）。

## CLI：直接測試 Solver

CLI 不使用 Web 或資料庫，讀取上月班表、本月需求、八週區間與非常態班型 CSV 後，直接呼叫 M 或 T Solver。需先安裝 .NET 10 SDK。

日期文字一律使用零補齊的 ISO 形式：完整日期 `yyyy-MM-dd`、月份 `yyyy-MM`、只有月日時 `MM-dd`；不接受斜線或省略前導零。

專案附有可直接執行的 M 與 T 範例：

```bash
# 站務 M
cd examples/m-2026-09
dotnet run --project ../../src/NtmScheduler.Cli

# 或檢測 T
cd examples/t-2026-09
dotnet run --project ../../src/NtmScheduler.Cli
```

兩個範例依序輸入：

```text
2026-09
previous.csv
demand.csv
rest-intervals.csv
non-standard-shifts.csv
```

五個問題分別是：

1. 目標月份，格式為 `yyyy-MM`。
2. 上月班表 CSV；留空代表沒有歷史，只適用於本月到職的新進人員。
3. 本月需求 CSV；留空使用目前目錄的 `demand.csv`。
4. 八週區間 CSV；留空使用目前目錄的 `rest-intervals.csv`。
5. 非常態班型 CSV；留空使用目前目錄的 `non-standard-shifts.csv`。

CLI 會依本月需求的「能力」與「T月班別」欄自動判斷單位：兩欄全部留空為 M，兩欄全部填寫為 T，不可混用。求解結果會顯示狀態、候選數量與各優先序分數，並在目前目錄輸出 `candidate-1.csv` 至 `candidate-3.csv`；M 有外派時另輸出 `candidate-N-external.csv`。既有候選檔只會詢問一次是否覆寫，`Ctrl+C` 可取消求解。

Exit code：有候選為 `0`；輸入錯誤、無解或沒有候選為 `1`；取消為 `130`。

### 月班表 CSV：`previous.csv` 與 `demand.csv`

兩個檔案使用相同且順序固定的 45 欄表頭：

```text
ID,姓名,所屬,到職日期,能力,T月班別,月初區間累計R,月初區間累計R1,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,當月R,當月R1,當月指定R休,月底區間累計R,月底區間累計R1,本月班數
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
| 當月指定R休 | `demand.csv` 填精確需求數，空白視為 0；歷史與候選填由日格重算的實際 R休數 |
| 月底區間累計R／R1 | 截至本月最後一日，在月底所屬八週區間內的累計數量；`previous.csv` 必填，`demand.csv` 必須留空 |
| 本月班數 | `previous.csv` 必填；`demand.csv` 必須留空 |

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

M 簡寫會在讀入時正規化：例如 `1早` 解讀為 `LB01早`、`12小` 或 `12午` 解讀為 `LB12小`；候選與外派 CSV 一律以 `小` 表示 M 午班。T 午班仍輸出 `午`。

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
dotnet build NtmScheduler.slnx
dotnet test NtmScheduler.slnx
```

## 專案結構（去哪找什麼）

| 想找什麼 | 路徑 |
|---|---|
| 程式進入點 | `src/NtmScheduler.Web/Program.cs` |
| Blazor 畫面 | `src/NtmScheduler.Web/Components/Pages/` |
| 資料庫 | `src/NtmScheduler.Infrastructure/Data/` |
| M 排班模型 | `src/NtmScheduler.Solvers/M/MModelBuilder.cs` |
| T 排班模型 | `src/NtmScheduler.Solvers/T/TModelBuilder.cs` |
| 硬規則 | `src/NtmScheduler.Core/Evaluation/Rules/HardRules.cs` |
| 軟規則（評估） | `.../Rules/MSoftRules.cs`、`TSoftRules.cs`、`GeneralSoftRules.cs` |
| 軟規則目錄（順序／說明） | `src/NtmScheduler.Core/Evaluation/RuleCatalog.cs` |
| CSV | `src/NtmScheduler.Infrastructure/Csv/` |
| 範例資料 | `src/NtmScheduler.Core/SampleData/DemoDataset.cs` |

更完整的導覽見 [`docs/12-code-tour.md`](docs/12-code-tour.md)。  
軟規則如何修改見 [`docs/13-soft-rules-guide.md`](docs/13-soft-rules-guide.md)。  
本次重構紀錄見 [`docs/14-refactoring-notes.md`](docs/14-refactoring-notes.md)。

## 技術棧

- ASP.NET Core Blazor（Interactive Server）、.NET 10
- Google OR-Tools CP-SAT（M／T 分開建模，軟規則逐條字典序最佳化）
- EF Core + SQLite（開發）；正式環境 DB 未定，故避免 provider 專屬語法

## 流程（無 Publish／Draft）

```
輸入（人員、R*、X、歷史、週期）
  → 建立 Run（背景求解）
  → 最多 3 份候選
  → 選一份成為「目前班表」
  → 寬表人工修改（自動存檔 + 重新驗證）
```

可選：建立快照／還原。沒有審核、沒有發布鎖定。

## 文件

業務規則與資料格式見 [`docs/`](docs/)。Agent 守則見 [`AGENTS.md`](AGENTS.md)。
