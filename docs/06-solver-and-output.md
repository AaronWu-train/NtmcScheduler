# Solver 流程、狀態與輸出

## 公開介面

```csharp
MSolveResult MSolver.Solve(
    ScheduleInput input,
    SolverOptions? options = null,
    CancellationToken cancellationToken = default);

MSolveResult MSolver.Solve(
    ScheduleInput input,
    MPerpetualSchedule perpetualSchedule,
    SolverOptions? options = null,
    CancellationToken cancellationToken = default);

TSolveResult TSolver.Solve(
    ScheduleInput input,
    SolverOptions? options = null,
    CancellationToken cancellationToken = default);
```

`CancellationToken` 是呼叫端中止信號，不是班表資料。取消時 solver 呼叫 `StopSearch` 並丟出 `OperationCanceledException`，不回傳部分結果。

`SolverOptions` 預設為五分鐘、random seed 0、8 workers。正常時間上限到期不丟例外。

## 原始碼流程

M 與 T 完全分開建模，各自保留 `Main / Input / Rules` 三個 partial 檔案。每個 `Solve` 的主幹依序為：

```text
複製並驗證 ScheduleInput
建立目標月 + 7 日的 CP-SAT 變數
加入硬限制
M 有萬年班表時，以模板加入 partial solution hint
先取得一份符合硬限制的初始解
清除模板 hint，改以初始可行解加入 incumbent hint
建立具名軟規則與權重
逐 Priority 字典序求解
搜尋最多 3 份差異候選
讀取目標月結果
```

沒有 rule class、catalog、definition、encoder、DI 或外部規則設定。

## 字典序求解

1. 在同一總時限內先求一份只符合硬限制的初始解。
   M 有萬年班表時，只對模板非空白格使用 `AddHint`；空白格不提供 hint。初始搜尋使用 `repair_hint` 與單 worker；hint 不是限制，不能保證候選遵循模板。清除模板後的各 Priority 搜尋仍使用 `SolverOptions.WorkerCount`。
2. 對當前 Priority 設定 `Minimize`。
3. 只有 CP-SAT 回傳 `Optimal` 時，加入 `objective == optimum`。
4. 繼續下一 Priority。
5. M 固定預留候選搜尋時間：總時限大於 60 秒時預留 30 秒，否則預留總時限的一半。任一組只得到 `Feasible` 時，保留當前 incumbent，並在預留時間內優先搜尋至少第二份不同候選；候選搜尋改為無品質目標的可行性搜尋，但每個具名目標分數都不得超過第一份的 120%。剩餘時間用完則回傳 `TimeLimit` 與已找到的候選，不再最佳化後續組；T 沿用原流程，不允許替代候選分數變差。
6. 全部優先組證明最佳後，才搜尋替代候選。

## 候選差異

- 最多 3 份。
- 只計算目標月、已到職、非固定的日格。
- 未決定 `R*` 不是固定格；已填正常班、R、R1、`R*[R]`、`R*[R1]`、`R*[R休]` 與 X 是固定格。
- M/T 每個新候選至少改變 `ceil(可比較格數 × 5%)`，並與每份既有候選分別達到門檻。M 在所有目標已證明最佳時維持相同目標分數；由 feasible incumbent 進入 TLE 備援搜尋時，每個 `ObjectiveScore.Value` 最多可比第一份高 20%，以整數不等式 `候選分數 × 5 <= 第一份分數 × 6` 判定。第一份分數為 0 時，替代候選該項也必須為 0。
- T 只在所有目標證明最佳且以等式固定後搜尋替代候選，因此每份 T 候選的 J1–J5 分數都與第一份相同，不套用 M 的 20% 放寬。
- 替代候選搜尋用完剩餘時間時保留已找到的候選；若所有目標已證明最佳則狀態為 `Optimal`，若由 feasible incumbent 進入替代搜尋則維持 `TimeLimit`。若第一份尚無完整具名目標分數，或不存在同時符合硬限制、5% 差異門檻與 M 20% 分數上限的第二解，或預留時間不足，只輸出第一份。

## 狀態

- `Optimal`：所有優先組皆已證明最佳。
- `TimeLimit`：求解時間到；可能帶有已找到的合法候選。候選中的 `ObjectiveScore` 是該 incumbent
  當下各組分數，不代表每一組皆已證明最佳；目前結果型別不另記錄最佳化進度。
- `Infeasible`：硬限制無解；本版不做 M 缺班或 T 衝突分析。
- `InvalidInput`：資料邊界驗證失敗，帶 `Field + Message`。

## 候選內容

每份 M/T 候選都包含：

- 完整目標月 `MonthlySchedule`；不輸出延伸日。
- 每個 Priority 的名稱、總分、組內違反量與實際權重。
- CLI 以繁體中文顯示狀態與各項名稱，逐項說明違反量的計算意義，並分別標示違反量、權重及加權分，不使用未標示意義的乘法算式。
- 每人月初區間累計、當月 R/R1、月底所屬區間累計與本月班數。
- R休以日格 `R*[R休]` 呈現；不另加跨月或 56 日累計。

M 候選另包含外派日期、車站、班別與人數。

## 薄型 CLI

```bash
dotnet run --project src/NtmScheduler.Cli
```

CLI 依序詢問目標月、上月 CSV、本月 CSV、八週區間 CSV 與非常態班型 CSV；偵測為 M 後再詢問可留白的八週萬年班表 CSV。CSV 只由 CLI 解析；solver 收到 typed snapshot。Ctrl+C 傳入 cancellation token。T 維持單次預設求解；M 使用 `--m-search workers=4,seeds=2,seconds=180` 控制每個 seed 的 workers、並行 seed 數與各次超時秒數，省略時採該組預設。Seed 固定使用 `0..seeds-1`，CLI 輸出第一候選具名目標字典序較佳的整批結果。結果摘要另以小數一位顯示 portfolio 的實際 wall time，不包含互動輸入、CSV 讀取與候選檔寫入時間。

透過 `dotnet run` 時，開關放在參數分隔符號之後：

```bash
dotnet run --project src/NtmScheduler.Cli -- --m-search workers=4,seeds=2,seconds=180
```

`workers` 是每個 seed 的 worker 數，因此最大並行 worker 數約為 `workers × seeds`。三個值都必須是正整數；未知、缺漏或重複欄位視為格式錯誤。此開關只適用於 M。

輸出為目前目錄的 `candidate-N.csv`。M 有外派時另寫同編號的 `candidate-N-external.csv`。若預定編號已有主檔或外派檔，CLI 不詢問也不覆寫，整批改用下一段連續可用編號。

Exit code：有候選為 0；輸入錯誤、無解或無候選為 1；取消為 130。
