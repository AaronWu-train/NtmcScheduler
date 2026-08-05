# 軟規則修改指南

軟規則採**逐條字典序**最佳化：依順序最小化違反量後固定，再處理下一條。**不要**改成加權總和。

單一真相來源：

- 目錄（名稱、白話說明、優先層、預設順序、違反量定義）：`src/NtmScheduler.Core/Evaluation/RuleCatalog.cs`
- 純 C# 評估（畫面驗證、交叉核對）：`src/NtmScheduler.Core/Evaluation/Rules/`
- OR-Tools 目標編碼：`src/NtmScheduler.Solvers/M/MModelBuilder.cs`、`.../T/TModelBuilder.cs`

## 要改一條 M 軟規則看哪裡

1. 目錄說明／預設順序：`RuleCatalog.cs`（`DefaultMSoftOrder` 與對應 `RuleInfo`）
2. 違反量與使用者訊息：`src/NtmScheduler.Core/Evaluation/Rules/MSoftRules.cs`（或共用規則的 `GeneralSoftRules.cs`）
3. Solver 目標：`src/NtmScheduler.Solvers/M/MModelBuilder.cs` 的 `EncodeSoftObjectives` 與對應 `Encode*` 方法
4. 網頁順序／開關：`/rules`（寫入 `RuleSettings` 表）；預設種子也來自 `RuleCatalog.DefaultRows`

## 要改一條 T 軟規則看哪裡

1. `RuleCatalog.cs`（`DefaultTSoftOrder`）
2. `src/NtmScheduler.Core/Evaluation/Rules/TSoftRules.cs`（或 `GeneralSoftRules.cs`）
3. `src/NtmScheduler.Solvers/T/TModelBuilder.cs`

## 如何調整規則順序

- **執行時（給管理者）**：網頁「規則管理」對 P2–P4 上移／下移後儲存。P0／P1 鎖定。
- **預設順序（給開發）**：改 `RuleCatalog.DefaultMSoftOrder` / `DefaultTSoftOrder`。清空該單位 `RuleSettings` 後重新開啟規則頁會重建預設。

## 如何增加參數

1. 在 `RuleCatalog` 的該條 `ParametersHelp` 寫清楚參數意義。
2. 規則頁的「參數 JSON」會存進 `RuleSettings.ParametersJson`，並經 `SoftRuleSpec.ParametersJson` 傳入 Solver。
3. 在對應 `Encode*`／`Evaluate` 讀取 JSON（目前多數規則尚無參數；新增時兩邊都要讀同一格式）。
4. **不要**為此新增 Mapper／DTO／註冊掃描；直接在規則檔解析即可。

## 如何新增一條軟規則

1. 在 `RuleCatalog.All` 與 `DefaultMSoftOrder`／`DefaultTSoftOrder` 加一筆。
2. 在 `MSoftRules.cs`／`TSoftRules.cs`／`GeneralSoftRules.cs` 加一個 `IRuleEvaluator` class（簡單規則集中同檔；複雜可再拆檔）。
3. 在 `RuleEvaluationEngine.CreateDefaultEvaluators` 註冊。
4. 在 `MModelBuilder`／`TModelBuilder` 的 `EncodeSoftObjectives` switch 加分支，回傳 `IntVar` 違反量。
5. 加測試：`tests/NtmScheduler.Tests/Core/`（評估器）與／或 Solvers 交叉核對。
6. 更新 `docs/05-soft-rules.md` 與本指南若有新慣例。

## 交叉核對

`LexicographicSolveEngine.ToCandidate` 會用 `RuleEvaluationEngine.EvaluateMetrics` 與模型目標比對，不一致會丟例外。改規則時兩邊公式必須一致。
