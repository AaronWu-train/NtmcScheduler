# 03. 輸入資料、歷史與驗證

## 1. 每次排班需要的資料

| 資料 | 最少必要內容 | 來源 |
|---|---|---|
| 基本資訊 | `unit`（M 或 T）、`targetMonth` | 操作介面 |
| M 人員 | `employeeId`、`name`、`homeStation` | 資料庫或 CSV |
| T 人員 | `employeeId`、`name`、`specialty`（可空白）、`ability`（1–5 整數） | 資料庫或 CSV |
| T 每月班別 | `employeeId`、`month`、`shift` | 資料庫或 CSV |
| R\* | `employeeId`、`date` | 操作介面或 CSV |
| X | `employeeId`、`start`、`end`、`description` | 操作介面或 CSV |
| 8 週週期表 | 每週期 `start`、`end`、`requiredR` | 管理者維護（固定設定） |
| 歷史 | 見下方第 4 節 | Published 班表＋X 事件 |

## 2. 建立排班請求（JSON 範例）

```json
{
  "unit": "T",
  "targetMonth": "2026-08",
  "events": [
    { "employeeId": "T001", "type": "R*", "date": "2026-08-12" },
    { "employeeId": "T002", "type": "X",
      "start": "2026-08-15T09:00:00+08:00",
      "end": "2026-08-15T17:00:00+08:00", "description": "上課" }
  ]
}
```

## 3. X 事件的資料規則

- X 可同日結束，或**跨一個午夜**；不接受跨超過兩個曆日的 X。
- X 歸屬其**開始日期**：寬表在開始日顯示 X、算 1 個工作日。
- 跨午夜 X 的**結束日期**那一格由求解器照常決定（早／午／夜／R／R\* 皆可）；
  該日若排正常班，必須通過 11 小時休息檢查（從 X 的結束時刻起算）。（決策 D-01）
- X **不補** M 正常班位；**不算** T 的正常出勤、專業出勤或能力平均。
- X 的完整起訖時間必須與前後的實際工作區間一起檢查 11 小時（GEN-H-03）。
- X 事件在連續工作區段中算工作日（只算開始日）；在 M 同班別區塊中完全略過。

## 4. 歷史資料

- 正式歷史來源是 **Published 班表＋X 完整事件**，不是單獨的 schedule.csv。
- 每次排班至少載入：**目前 8 週週期起始日**（若排班區間橫跨多個週期，取最早相交週期的起始日）
  至**排班區間前一天**的每日狀態。
- 另需取得每位人員的：
  - 上一次**實際工作結束時間**（供月初的 11 小時檢查）；
  - M 尚未結束的**上一個同班別區塊**（班別與目前次數，供承接）。
- 以 CSV 匯入歷史時，若含 X，必須同時匯入 `events.csv` 才能重建完整時間，否則為 `INVALID_INPUT`。
- **初次上線**：提供「歷史匯入」功能，人工整理過去至少 8 週的 `schedule.csv`＋`events.csv`
  匯入後才能排班；歷史不足時系統回報資料不足（`INVALID_INPUT`），不自行猜測。（決策 D-05）

## 5. CSV 格式（一律 UTF-8 with BOM）

### m_employees.csv

```csv
employee_id,name,home_station
M001,王小明,LB01
```

### t_employees.csv

```csv
employee_id,name,specialty,ability
T001,陳小明,軌道,4
```

### t_monthly_shift.csv

```csv
employee_id,month,shift
T001,2026-08,夜
T001,2026-09,早
```

### events.csv

R\* 只填 `date`；X 只填 `start`／`end`／`description`。

```csv
employee_id,type,date,start,end,description
M001,R*,2026-08-12,,,
M002,X,,2026-08-15 09:00,2026-08-15 17:00,上課
```

### schedule.csv（匯出；也是歷史匯入格式）

```csv
employee_id,name,home_station,2026-08-30,2026-08-31,2026-09-01,month_r,cycle_r
M001,王小明,LB01,早,R*,LB02-午,1,12
M002,陳小華,LB02,X,午,R,1,11
```

- 日期欄可包含延伸排班日。T 的檔案將 `home_station` 欄改為 `shift`（當月班組）。
- `month_r`、`cycle_r` 等統計欄**只供顯示**；任何後續計算必須從每日狀態與完整 X 事件重新計算。

## 6. 輸入驗證：INVALID_INPUT 完整清單

以下任一情況，排班請求直接回傳 `scheduleStatus = INVALID_INPUT`，並附上可定位的訊息
（涉及的員工、日期或欄位）。**事件彼此衝突不是交給求解器選擇，而是輸入錯誤。**

| # | 情況 |
|---|---|
| 1 | 同一人同一日同時有 R\* 與 X（X 以開始日期歸屬） |
| 2 | 同一人兩筆事件重疊（兩筆 X 時間區間重疊；同日重複 R\*） |
| 3 | X 的結束時間不晚於開始時間 |
| 4 | X 跨超過兩個曆日（跨兩個以上午夜） |
| 5 | R\* 的日期或 X 的開始日期不在排班區間內 |
| 6 | 新 R\*／X 與**已發布**的日期衝突（該日已有 Published 狀態）；需先走人工版本流程，不能默默覆蓋（GEN-H-05） |
| 7 | 某人某週期的 R\* 過多，使 8 週 R（GEN-H-04）必然無法滿足：歷史已累積 R＋該週期內 R\* 數 > `requiredR`。訊息需指出員工與週期（決策 D-10） |
| 8 | T 人員 `ability` 不是 1–5 的整數；或目標月缺少該員的月班別資料 |
| 9 | M 人員 `homeStation` 不在 LB01–LB12 |
| 10 | `employeeId` 重複；或事件引用不存在的員工 |
| 11 | 歷史不足（未涵蓋第 4 節要求的範圍），或歷史含 X 但缺 events.csv |
| 12 | 8 週週期表缺漏：排班區間有日期不屬於任何週期，或週期彼此重疊 |
| 13 | CSV 格式錯誤：欄位缺漏、日期／時間無法解析、月份格式錯誤 |
