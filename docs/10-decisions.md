# 決策紀錄

## 2026-08-07：月班表成為 solver 資料邊界

- CSV／Excel 的 domain 名稱是「月班表」，C# 使用 `MonthlySchedule` 與 `EmployeeMonthlySchedule`。
- M/T 共用 `ScheduleInput(PreviousMonth, DemandMonth, RestIntervals)`，移除舊的員工、固定指派、七天歷史、班組與額度平行輸入。
- `DemandMonth` 是本月人員唯一來源，不建員工主檔。
- Excel 可使用相同欄位；本版 CLI 只實作 CSV。

## 2026-08-07：歷史與新進人員

- 歷史提供完整上月，不只是前七天。
- 本月有、上月沒有者必須是本月到職；到職日早於本月卻無歷史為無效輸入。
- 新進者在到職前的區間六日數與國定假日數，分別視為已計 R 與 R1。
- 月中到職不參與整月公平性，但到職後適用每日規則與個人月目標。

## 2026-08-07：八週區間

- 區間為連續 56 日，R 額度由 16 個六日推導，R1 額度為區間國定假日數。
- 六日不會同時列為國定假日。
- 月中切換 A/B 時，Opening 屬 A，Closing 屬 B。
- 本月 R 與 R1 軟目標由到職後本月六日／國定假日推導，不再手動輸入。

## 2026-08-07：跨月規則

- 兩個 solver 都固定建模到月底後七天，但不輸出延伸日。
- M 的夜–休–早／午計算所有與目標月相交的三日視窗。
- M 同班別區塊只看目標月、只比時段不比車站，並在月底結算。
- T 夜轉早從上月實際班表找最後夜班；沒有實際夜班就不產生跨月不足量。
- T 下月班別依早 → 午 → 夜 → 早輪轉。

## 2026-08-07：原始碼與 CLI

- 不建立 rule class、catalog、definition、encoder、DI 或外部規則設定。
- M/T 各使用 Main／Input／Rules 三個 partial 檔，建模邏輯不共用。
- C# 不放 Rule ID 或 J1–J5；Objective 使用 `Priority + 有意義名稱`。
- CLI 只有四個互動輸入，不加 flags、JSON、DI 或額外套件。
- 本版 M/T 都不做無解原因分析。
