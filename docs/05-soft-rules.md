# 軟規則與字典序目標

Solver 依 `Priority` 由小到大逐組最小化。只有目前組證明 `Optimal` 後，才固定該組最佳值並繼續下一組。權重只用於同一優先組內比較，不會跨組取代字典序。

C# 不使用 J1–J5 或 Rule ID；輸出 `Priority + 有意義名稱`。

## 共用定義

### 指定休假

`R*` 最後不是 R、R1 或 R休，違反量 +1。

### 未使用 R休

每人的 `當月指定R休` 是可使用上限；實際 R休 每少於上限 1 日，違反量 +1。

### 本月休假分配

每人目標不由輸入手動設定：

- R 目標 = 到職後、本月的週六與週日天數，依實際月曆計算，不固定為 8。
- R1 目標 = 到職後、本月的國定假日數。

實際數與目標差 0 或 1 不罰；超過 1 的部分平方計分。

### 工作連續長度

正常班與 X 都是實際工作，R/R1/R休 結束連續。一個已結束區塊的長度罰分為：

| 長度 | 0 | 1 | 2 | 3 | 4–5 | 6 以上 |
|---:|---:|---:|---:|---:|---:|---:|
| 違反量 | 0 | 4 | 2 | 1 | 0 | `2 × (長度 - 5)` |

工作連續長度會從完整上月末端接續。

## M 目標

| Priority | 名稱 | 組內項目與權重 |
|---:|---|---|
| 1 | `RequestedRest` | RequestedRest ×3；UnusedLeaveRest ×1 |
| 2 | `ExternalStaffing` | ExternalStaffing ×1 |
| 3 | `MonthlyRestDistribution` | MonthlyRest ×4；MonthlySpecialRest ×8 |
| 4 | `ScheduleQuality` | NonHomeStation ×8；WorkStreak ×3；SameShiftBlock ×2；NightRestEarly ×12；NightRestAfternoon ×8；ShiftChangeWithoutRest ×6 |
| 5 | `RotationAndFairness` | NonPreferredRotation ×1；WeekdayRestFairness ×2；HolidayRestFairness ×4；SupportFairness ×3；EarlyShiftFairness ×1；AfternoonShiftFairness ×1；NightShiftFairness ×2 |

### M 各項違反量

- **ExternalStaffing**：目標月外派總人次。
- **NonHomeStation**：不在所屬站工作的日數；仍必須位於合法三站群組。
- **SameShiftBlock**：只看目標月，只比較早／午／夜時段，不比較車站；R、R1、R休、X 在班別序列中略過。月底依上表結算最後區塊，不與上月併接。
- **NightRestEarly / NightRestAfternoon**：計數「夜班 → R/R1/R休 → 早班／午班」三日視窗。凡視窗與目標月相交就計算，因此可使用上月末與月底延伸日。
- **ShiftChangeWithoutRest**：兩個有效正常班之間沒有 R/R1/R休，且班別不同，計 +1；X 略過。
- **NonPreferredRotation**：班別改變不是早 → 午、午 → 夜或夜 → 早，計 +1；R/R1/R休/X 略過。
- **Weekday/HolidayRestFairness**：同車站群組人員的休假數最大差。假日包含六日與國定假日。
- **SupportFairness**：同車站群組人員的跨站支援數最大差。
- **Early/Afternoon/NightShiftFairness**：只比較同車站群組、整月在職人員的目標月正常班數。每群每班以 `n × 平方和 - 總和²` 衡量離散程度；早、午、夜權重依序為 1、1、2。X 與延伸日不計。

## T 目標

| Priority | 名稱 | 組內項目與權重 |
|---:|---|---|
| 1 | `RequestedRest` | RequestedRest ×3；UnusedLeaveRest ×1 |
| 2 | `StaffingQuality` | NonMonthlyShift ×9；Attendance ×9；Specialty ×3；Ability ×1 |
| 3 | `MonthlyRestDistribution` | MonthlyRest ×1；MonthlySpecialRest ×1 |
| 4 | `WorkPatternQuality` | WorkStreak ×3；NightToEarlyRest ×12；MonthBoundaryRestBalance ×5 |
| 5 | `RestFairness` | WeekdayRestFairness ×2；HolidayRestFairness ×4 |

### T 各項違反量

- **NonMonthlyShift**：每個模型日期的正常工作班別不同於該日期月班別，計 +1；目標月以 `T月班別` 為基準，月底後七天以早 → 午 → 夜 → 早的下月輪轉班別為基準。X 不計。
- **Attendance**：每日每月班組的實際出勤人數，低於當日在職組員數一半（整數除法）的缺口；跨班人員計入實際工作的班別。
- **Specialty**：一個月班組原有的專業分組，當日沒有任何該專業人員在該班正常出勤，計 +1；跨班人員可補足實際工作班別的專業。
- **Ability**：實際在該班正常出勤人員的總能力低於 `3 × 出勤人數` 的差額；跨班人員計入實際工作班別。
- **NightToEarlyRest**：本月為早班者，從上月實際班表找最後一個夜班，到本月第一個實際早班前少於兩天 R/R1/R休 的差額。上月沒有實際夜班時不產生此違反量。
- **MonthBoundaryRestBalance**：有實際夜轉早的人員，比較上月最後一日與本月第一日休假人數差。
- **Weekday/HolidayRestFairness**：同一 T 月班別內的休假數最大差。

月中到職人員不納入 M/T 公平性範圍。

R休納入所有實際休假公平性，但不納入 R/R1 的本月分配目標。
