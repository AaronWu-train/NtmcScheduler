```bash
cd examples/m-2026-09
dotnet run --project ../../src/NtmcScheduler.Cli
```

六個互動答案：

```text
2026-09
previous.csv
demand.csv
rest-intervals.csv
non-standard-shifts.csv
m-perpetual.csv
```

`previous.csv` 保存每位員工的萬年班表代號，`demand.csv` 留白並由 solver 自動承接。模板第 1 日 8 週 16R 休假區間的開始日；模板日留白代表該日不提供 hint。

預期產生 `candidate-1.csv` 與 `candidate-1-external.csv`。
