# 軟規則與字典序目標

Solver 依 `Priority` 由小到大逐組最小化。只有目前組證明 `Optimal` 後，才固定該組最佳值並繼續下一組。權重只用於同一優先組內比較，不會跨組取代字典序。

C# 不使用 J1–J5 或 Rule ID；輸出 `Priority + 有意義名稱`。

## 共用定義

### 指定休假

`R*` 最後不是 R 也不是 R1，違反量 +1。

### 本月休假分配

每人目標不由輸入手動設定：

- R 目標 = 到職後、本月的六日數。
- R1 目標 = 到職後、本月的國定假日數。

實際數與目標差 0 或 1 不罰；超過 1 的部分平方計分。

### 工作連續長度

正常班與 X 都是實際工作，R/R1 結束連續。一個已結束區塊的長度罰分為：

| 長度 | 0 | 1 | 2 | 3 | 4–5 | 6 以上 |
|---:|---:|---:|---:|---:|---:|---:|
| 違反量 | 0 | 4 | 2 | 1 | 0 | `2 × (長度 - 5)` |

工作連續長度會從完整上月末端接續。

## M 目標

| Priority | 名稱 | 組內項目與權重 |
|---:|---|---|
| 1 | `RequestedRest` | RequestedRest ×1 |
| 2 | `ExternalStaffing` | ExternalStaffing ×1 |
| 3 | `MonthlyRestDistribution` | MonthlyRest ×4；MonthlySpecialRest ×8 |
| 4 | `ScheduleQuality` | NonHomeStation ×8；WorkStreak ×3；SameShiftBlock ×2；NightRestEarly ×12；NightRestAfternoon ×8；ShiftChangeWithoutRest ×6 |
| 5 | `RotationAndFairness` | NonPreferredRotation ×1；WeekdayRestFairness ×2；HolidayRestFairness ×4；SupportFairness ×3 |

### M 各項違反量

- **ExternalStaffing**：目標月外派總人次。
- **NonHomeStation**：不在所屬站工作的日數；仍必須位於合法三站群組。
- **SameShiftBlock**：只看目標月，只比較早／午／夜時段，不比較車站；R、R1、X 在班別序列中略過。月底依上表結算最後區塊，不與上月併接。
- **NightRestEarly / NightRestAfternoon**：計數「夜班 → R/R1 → 早班／午班」三日視窗。凡視窗與目標月相交就計算，因此可使用上月末與月底延伸日。
- **ShiftChangeWithoutRest**：兩個有效正常班之間沒有 R/R1，且班別不同，計 +1；X 略過。
- **NonPreferredRotation**：班別改變不是早 → 午、午 → 夜或夜 → 早，計 +1；R/R1/X 略過。
- **Weekday/HolidayRestFairness**：同車站群組人員的休假數最大差。假日包含六日與國定假日。
- **SupportFairness**：同車站群組人員的跨站支援數最大差。

## T 目標

| Priority | 名稱 | 組內項目與權重 |
|---:|---|---|
| 1 | `RequestedRest` | RequestedRest ×1 |
| 2 | `StaffingQuality` | Attendance ×9；Specialty ×3；Ability ×1 |
| 3 | `MonthlyRestDistribution` | MonthlyRest ×1；MonthlySpecialRest ×1 |
| 4 | `WorkPatternQuality` | WorkStreak ×3；NightToEarlyRest ×12；MonthBoundaryRestBalance ×5 |
| 5 | `RestFairness` | WeekdayRestFairness ×2；HolidayRestFairness ×4 |

### T 各項違反量

- **Attendance**：每日每月班組的實際出勤人數，低於當日在職組員數一半（整數除法）的缺口。
- **Specialty**：一個月班組中，當日沒有任一位該專業分組人員出勤，計 +1。
- **Ability**：當日出勤人員總能力低於 `3 × 出勤人數` 的差額。
- **NightToEarlyRest**：本月為早班者，從上月實際班表找最後一個夜班，到本月第一個實際早班前少於兩天 R/R1 的差額。上月沒有實際夜班時不產生此違反量。
- **MonthBoundaryRestBalance**：有實際夜轉早的人員，比較上月最後一日與本月第一日休假人數差。
- **Weekday/HolidayRestFairness**：同一 T 月班別內的休假數最大差。

月中到職人員不納入 M/T 公平性範圍。
