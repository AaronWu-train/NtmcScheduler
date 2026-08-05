# 重構紀錄（2026-08）

目標：簡單、好懂、好用。以實際程式為準，不做制式分層堆疊。

## 原架構問題

1. **過度抽象**：15 個服務介面全部只有一個實作；平行 enum（生命週期／班表狀態）靠手寫 mapping。
2. **流程半成品**：Draft→Publish 狀態機完整存在於 UI／DB，但 `DraftService.RevalidateAsync` 是 stub（永遠 fail-closed），實際上無法發布。
3. **歷史未接上**：`ScheduleRunWorker` 組 `SolveRequest` 時 `Histories` 曾為空字典，GEN-H-05／銜接規則在正式路徑名存實亡。
4. **軟規則雙來源**：`RuleEvaluationEngine.DefaultSoftRules` 與 `RuleSettingService` 預設不一致（T 缺 GEN-S-STREAK）。
5. **檔案過散**：26 個規則各一檔，加上 DTO／介面／頁面，不熟悉 .NET 時難以定位。
6. **CSV 被迫感**：事件與歷史依賴多份 CSV；網頁缺少白話說明與範本。

## 選擇的重構方式與原因

- **保留 Core／Solvers／Infrastructure／Web 四專案**：AGENTS.md 要求 Core 不得參考 OR-Tools／EF；合併專案收益不大，但合併規則檔與刪除 Publish 流程收益大。
- **規則集中**：硬規則一檔、M／T／共用軟規則各一檔；`RuleCatalog` 作為順序／說明／種子的單一來源。
- **流程改為「候選 → 目前班表」**：刪除 Draft／Publish／OfficialScheduleVersion 業務路徑，改 `MonthSchedule`＋可選 `ScheduleSnapshot`；驗證真正接上 `RuleEvaluationEngine`。
- **範例資料一鍵載入**：`DemoDataset`＋`DemoDataSeeder`，首頁按鈕即可跑通 M／T。
- **不建插件／反射／DSL**：軟規則維持明確 class＋switch。

## 主要合併／重寫／刪除

| 動作 | 內容 |
|---|---|
| 合併 | `Rules/Hard/*.cs` → `HardRules.cs`；Soft 各檔 → `MSoftRules`／`TSoftRules`／`GeneralSoftRules` |
| 新增 | `RuleCatalog.cs`、`MonthSchedule`／`ScheduleEdit`／`ScheduleSnapshot`、`ScheduleService`、`ScheduleContextBuilder`、`ScheduleEditor.razor`、`DemoDataset`／`DemoDataSeeder` |
| 刪除 | `IDraftService`／`DraftService`、`IPublishService`／`PublishService`、`DraftSchedule`／`DraftEdit`／`OfficialScheduleVersion`、`DraftEditor.razor` |
| 改寫 | Worker 載入歷史；候選「選為目前班表」；規則頁顯示目錄說明；Events 頁白話說明；Nav／Home／Runs／Versions 文案 |
| 遷移 | `ReplaceDraftPublishWithMonthSchedule`（或同名 MonthScheduleWorkflow） |

## 使用者如何完成一次排班

見 README「載入範例並完成一次排班」。正常流程不需 CSV。

## 範例資料

- M：12 站；夜班站與 C 群略多人力；歷史含 R／R*／R1／跨站／同日 X／跨午夜 X
- T：早午夜各 10 人；專業與能力；月班組；R* 與 X
- 目標月預設 `2026-08`

## 網頁新增說明

- Events：R*／R1／X／R 差異與例子
- Rules：目錄說明、違反量定義、修改指引
- Home：載入範例說明
- Candidates／Runs：目前班表流程文案

## 尚未完成／已知限制

- 未把 Infrastructure 實體合併進 Web 專案（仍四專案，但入口與導覽文件已對齊）。
- 多數單實作 `I*Service` 介面仍在（為減少一次大爆炸 diff）；可後續再刪。
- Run 的完整可重現 SnapshotJson 仍偏精簡（Worker 已讀 DB 歷史，但快照欄位未存全量輸入）。
- 尚無瀏覽器 E2E；以單元／整合／Solver acceptance 為主。
- CSV 工具頁的「空白範本／完整範例下載／預覽確認」可再加強（人員頁既有匯入；歷史匯入頁仍可用）。
