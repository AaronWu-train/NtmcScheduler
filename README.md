# NtmScheduler

新北捷運人員排班系統。依人員資料、指定休假（R*）、公務事件（X）與歷史班表，以 OR-Tools CP-SAT 產生最多三份月班表候選；使用者選一份成為「目前班表」，可在寬表中隨時修改，每次修改自動保存並重新檢查規則。

目前支援站務（M）與檢測（T）兩個單位。

## 環境需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## 快速開始

```bash
dotnet run --project src/NtmScheduler.Web --launch-profile http
```

瀏覽器開啟 http://localhost:5109。

開發環境使用 SQLite，首次啟動會自動建立／遷移 `ntm.db`。

### 載入範例並完成一次排班

1. 開啟首頁，點「**載入範例資料**」（會寫入 12 站站務人員、約 30 名檢測人員、R*／X、歷史班表、8 週週期）。
2. 確認目標月為 `2026-08`，準備狀態顯示可建立 Run。
3. 前往「求解 Run」→ 選單位（M 或 T）→「建立 Run」。
4. 等待求解完成 →「候選比較」→「選為目前班表」。
5. 在寬表點格子修改；修改會自動保存並重新驗證。

一般流程**不需要**上傳 CSV。CSV 僅作選用的批次工具（人員、寬表匯入／匯出）。

## 建置與測試

```bash
dotnet build NtmScheduler.slnx
dotnet test NtmScheduler.slnx
```

## 專案結構（去哪找什麼）

| 想找什麼 | 路徑 |
|---|---|
| 程式進入點 | `src/NtmScheduler.Web/Program.cs` |
| Blazor 畫面 | `src/NtmScheduler.Web/Components/Pages/` |
| 資料庫 | `src/NtmScheduler.Infrastructure/Data/` |
| M 排班模型 | `src/NtmScheduler.Solvers/M/MModelBuilder.cs` |
| T 排班模型 | `src/NtmScheduler.Solvers/T/TModelBuilder.cs` |
| 硬規則 | `src/NtmScheduler.Core/Evaluation/Rules/HardRules.cs` |
| 軟規則（評估） | `.../Rules/MSoftRules.cs`、`TSoftRules.cs`、`GeneralSoftRules.cs` |
| 軟規則目錄（順序／說明） | `src/NtmScheduler.Core/Evaluation/RuleCatalog.cs` |
| CSV | `src/NtmScheduler.Infrastructure/Csv/` |
| 範例資料 | `src/NtmScheduler.Core/SampleData/DemoDataset.cs` |

更完整的導覽見 [`docs/12-code-tour.md`](docs/12-code-tour.md)。  
軟規則如何修改見 [`docs/13-soft-rules-guide.md`](docs/13-soft-rules-guide.md)。  
本次重構紀錄見 [`docs/14-refactoring-notes.md`](docs/14-refactoring-notes.md)。

## 技術棧

- ASP.NET Core Blazor（Interactive Server）、.NET 10
- Google OR-Tools CP-SAT（M／T 分開建模，軟規則逐條字典序最佳化）
- EF Core + SQLite（開發）；正式環境 DB 未定，故避免 provider 專屬語法

## 流程（無 Publish／Draft）

```
輸入（人員、R*、X、歷史、週期）
  → 建立 Run（背景求解）
  → 最多 3 份候選
  → 選一份成為「目前班表」
  → 寬表人工修改（自動存檔 + 重新驗證）
```

可選：建立快照／還原。沒有審核、沒有發布鎖定。

## 文件

業務規則與資料格式見 [`docs/`](docs/)。Agent 守則見 [`AGENTS.md`](AGENTS.md)。
