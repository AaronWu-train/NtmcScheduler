# 06. 求解流程、候選、無解與輸出格式

## 1. 求解流程

1. 建立 ScheduleRun：先寫入資料庫（含完整快照），由 BackgroundService 依序執行，一次一個 Solver。
2. 輸入驗證：任何錯誤即回 `INVALID_INPUT`，不進入求解（清單見 `03` 第 6 節）。
3. 依 `unit` 選擇 M 或 T 的 ModelBuilder，建立 CP-SAT 模型（變數範圍：排班區間 × 人員；
   Published 歷史與 X 為固定值）。
4. 依 `05-soft-rules.md` 第 8 節逐條最佳化。
5. 產生最多 3 份**差異化**嚴格候選（見第 3 節）。
6. 寫回結果：候選、Coverage、違規指標、`result.json` 狀態。

## 2. 結果狀態

| 欄位／狀態 | 意義 |
|---|---|
| `scheduleStatus = FEASIBLE` | 至少存在 1 份嚴格候選 |
| `scheduleStatus = INFEASIBLE` | 已證實嚴格模型無解 |
| `scheduleStatus = INVALID_INPUT` | 輸入、歷史或固定事件錯誤 |
| `optimizationStatus = OPTIMAL` | 已執行的規則均證明最佳 |
| `optimizationStatus = TIME_LIMIT` | 有合法解，但某規則尚未證明最佳（其後規則未處理） |
| `candidateCount = 0–3` | 實際嚴格候選數；不足 3 份**不代表**候選違法 |

- `candidateCount = 0` 只會伴隨 `INFEASIBLE` 或 `INVALID_INPUT`；`FEASIBLE` 時必 ≥ 1（決策 D-14）。

## 3. 最多三份差異候選

- 先固定已完成的 P1–P4 品質結果（含 TIME_LIMIT 規則的目前最佳值），再尋找差異方案；
  **不得放寬 P0 或已固定的高順位品質**。
- 差異分母：可決定的「人員 × 日期」格，且**只計目標月份內的日期**
  （延伸日不參與差異比較，決策 D-13）。排除固定 X 的格與已發布固定日；
  比較時 R 與 R\* 視為**相同**休假結果。
- 最低差異格數 ＝ `ceil(分母 × 10%)`。
- 第 2 份與第 1 份差異達標；第 3 份須**分別**與第 1、第 2 份達標。
- 找不到 3 份可回傳 1 或 2 份。

## 4. 嚴格無解時的處理

### M：缺班分析（ShortageAnalysis）

- M 嚴格模型證實 `INFEASIBLE` 時，另建立缺班分析：**只放寬班位覆蓋（M-H-02）**，
  允許 `UNASSIGNED` 並最小化缺額總數；其餘 P0 照常。
- 結果只能查看（哪些站、日、班缺人），**不能成為 Draft 或 Published**。
- `result.json` 的 `shortageAnalysisAvailable = true`。

### T：衝突摘要

- T 硬規則無解時回傳衝突摘要。第一版最低要求：`INFEASIBLE` 狀態＋涉及的週期與人員
  基本統計（如各週期剩餘可休天數、R\* 總數、各班組人數）＋文字說明；
  不做最小衝突規則組合分析（決策 D-11）。

## 5. 輸出檔格式

### schedule.csv

見 `03-data-and-validation.md` 第 5 節（同一格式用於匯出與歷史匯入）。

### coverage.csv（M）

```csv
date,location,shift,required,assigned,external,unassigned
2026-08-31,LB02,午,1,0,1,0
```

- `assigned`＝內部指派數、`external`＝外派補足數、`unassigned`＝缺額（僅缺班分析會 > 0）。

### t_coverage.csv（T；欄位為本專案定義，決策 D-14）

```csv
date,shift,group_size,normal_attend,attend_target,avg_ability,missing_specialties
2026-08-31,午,11,4,5,2.75,軌道|號誌
```

- `missing_specialties` 以 `|` 分隔；無缺席專業則留空。

### violations.csv

```csv
solution_id,rule_id,date,employee_id,message
1,T-S-ABILITY,2026-08-31,,午班平均能力 2.75，低於 3
```

- `date`、`employee_id` 可為空（整體性指標）；`message` 必須是白話說明。

### result.json

```json
{ "scheduleStatus": "FEASIBLE", "optimizationStatus": "OPTIMAL",
  "candidateCount": 2, "shortageAnalysisAvailable": false }
```

## 6. 可重現性

每次 ScheduleRun 保存：人員、事件、歷史截止點、固定設定、規則順序與參數、
隨機種子（seed）、程式版本。相同快照＋相同 seed 重跑應得到相同結果；
系統重啟後 Queued／Running 工作依快照重新開始（不宣稱續跑原搜尋狀態）。
