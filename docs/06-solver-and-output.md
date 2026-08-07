# Solver 流程、狀態與輸出

## 公開介面

```csharp
MSolveResult MSolver.Solve(
    ScheduleInput input,
    SolverOptions? options = null,
    CancellationToken cancellationToken = default);

TSolveResult TSolver.Solve(
    ScheduleInput input,
    SolverOptions? options = null,
    CancellationToken cancellationToken = default);
```

`CancellationToken` 是呼叫端中止信號，不是班表資料。取消時 solver 呼叫 `StopSearch` 並丟出 `OperationCanceledException`，不回傳部分結果。

`SolverOptions` 預設為五分鐘、random seed 0、單 worker。正常時間上限到期不丟例外。

## 原始碼流程

M 與 T 完全分開建模，各自保留 `Main / Input / Rules` 三個 partial 檔案。每個 `Solve` 的主幹依序為：

```text
複製並驗證 ScheduleInput
建立目標月 + 7 日的 CP-SAT 變數
加入硬限制
建立具名軟規則與權重
逐 Priority 字典序求解
搜尋最多 3 份差異候選
讀取目標月結果
```

沒有 rule class、catalog、definition、encoder、DI 或外部規則設定。

## 字典序求解

1. 對當前 Priority 設定 `Minimize`。
2. 只有 CP-SAT 回傳 `Optimal` 時，加入 `objective == optimum`。
3. 繼續下一 Priority。
4. 任一組只得到 `Feasible` 或剩餘時間用完，回傳 `TimeLimit` 與當前可用候選，不再最佳化後續組。
5. 全部優先組證明最佳後，才搜尋替代候選。

## 候選差異

- 最多 3 份。
- 只計算目標月、已到職、非固定的日格。
- 未決定 `R*` 不是固定格；已填正常班、R、R1、`R*[R]`、`R*[R1]` 與 X 是固定格。
- 每個新候選至少改變 `ceil(可比較格數 × 10%)`，且已加入的差異限制會保留。
- 替代候選搜尋用完剩餘時間時，保留已找到的候選；前述最佳化狀態仍為 `Optimal`。

## 狀態

- `Optimal`：所有優先組皆已證明最佳。
- `TimeLimit`：求解時間到；可能帶有已找到的候選。
- `Infeasible`：硬限制無解；本版不做 M 缺班或 T 衝突分析。
- `InvalidInput`：資料邊界驗證失敗，帶 `Field + Message`。

## 候選內容

每份 M/T 候選都包含：

- 完整目標月 `MonthlySchedule`；不輸出延伸日。
- 每個 Priority 的名稱、總分、組內違反量與實際權重。
- 每人月初區間累計、當月 R/R1、月底所屬區間累計與本月班數。

M 候選另包含外派日期、車站、班別與人數。

## 薄型 CLI

```bash
dotnet run --project src/NtmScheduler.Cli
```

CLI 依序詢問目標月、上月 CSV、本月 CSV 與八週區間 CSV。依能力與 T 月班別欄自動判斷 M/T；Ctrl+C 傳入 cancellation token。

輸出為目前目錄的 `candidate-1.csv`–`candidate-3.csv`。M 有外派時另寫 `candidate-N-external.csv`。既有輸出檔時只詢問一次是否覆寫。

Exit code：有候選為 0；輸入錯誤、無解或無候選為 1；取消為 130。
