# Solver 與 CLI 實作結構

## 專案

```text
src/
├── NtmScheduler.Solvers/
│   ├── SolverContracts.cs
│   ├── MContracts.cs
│   ├── MSolver.cs
│   ├── MSolver.Input.cs
│   ├── MSolver.HardRules.cs
│   ├── MSolver.SoftRules.cs
│   ├── TContracts.cs
│   ├── TSolver.cs
│   ├── TSolver.Input.cs
│   ├── TSolver.HardRules.cs
│   └── TSolver.SoftRules.cs
└── NtmScheduler.Cli/
    ├── Program.cs
    └── ScheduleCsv.cs
```

`NtmScheduler.Solvers` 是 `.NET 10` class library，唯一外部相依是 Google OR-Tools `9.15.6755`。`NtmScheduler.Cli` 是 `.NET 10` console app，只參考 solver，CSV 使用 .NET standard library `TextFieldParser`。

## Solver 檔案責任

- `SolverContracts.cs`：共用的 `ScheduleInput`、月班表、日格、區間、選項、狀態與 Objective 輸出。無建模函數。
- `MContracts.cs` / `TContracts.cs`：候選與求解結果；M 另有外派輸出。
- `MSolver.cs` / `TSolver.cs`：公開 `Solve`、固定 Priority 群組的字典序 CP-SAT 呼叫、候選差異與結果讀取。
- `*.Input.cs`：快照複製、月份／人員／日格／區間驗證、歷史查詢與 R/R1 累積推導。
- `*.HardRules.cs`：OR-Tools 變數與不可關閉的硬限制。
- `*.SoftRules.cs`：軟違反量、固定群組及權重；M 為 J1、J4、J5，T 為 J1–J5。

M 與 T 不共用任何建模邏輯。每個 solver 只有一個 private `Variables` record 收納 OR-Tools 變數，不建 `Variables/Constraints/Objectives/Candidates` class 層。

## CLI 責任

`ScheduleCsv.cs` 是 CSV 邊界：讀寫月班表，讀取八週區間、M 萬年班表與非常態班型，並把文字格值轉為 solver contracts。`Program.cs` 只做：

1. 詢問五個共用輸入；M 再詢問可留白的萬年班表 CSV。
2. 建立 `ScheduleInput`。
3. 依能力／T 月班別判斷 M 或 T。
4. 傳入 Ctrl+C cancellation token。
5. 顯示狀態與具名分數，寫出候選。

CLI 不包含業務規則，solver 不知道 CSV 路徑。

## 測試

`tests/NtmScheduler.Solvers.Tests` 使用 MSTest，只參考 CLI 與 solver。測試重點為：

- typed input 與 InvalidInput 邊界。
- M/T 至少一個可求解主要案例。
- CSV round-trip、舊表頭相容、M 萬年班表、BOM、quoted field、非常態班型、跨午夜 X 與非法格值。
- 新進人員、A/B 區間累積、八週區間驗證。
- timeout、cancellation、CLI redirected stdin 與 examples smoke test。

## 可執行範例

`examples/m-2026-09` 與 `examples/t-2026-09` 都包含 `previous.csv`、`demand.csv`、`rest-intervals.csv`、`non-standard-shifts.csv` 與最小 README。兩組均由 solver 生成已驗證的合成需求，可直接交給 CLI 做快速 smoke test。範例假日不代表正式政府行事曆。

## 不在本階段

CLI flags、JSON、`.xlsx` 讀寫、Web、DB、DI、rule catalog，以及無解原因分析都不在此 solver 重寫範圍。
