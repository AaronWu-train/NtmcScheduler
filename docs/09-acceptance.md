# Solver 與 CLI 驗收

## 建置

- `dotnet build NtmScheduler.slnx -c Release` 成功。
- `dotnet test NtmScheduler.slnx -c Release` 成功。
- 不修改、不編譯 `tex/` 或 PDF。

## CSV

- 月班表可 round-trip，且支援 UTF-8 BOM、逗號／雙引號 quoted field 與 1–31 號欄。
- 需求列 `當月指定R休` 空白視為 0；歷史／候選列會核對實際 R休數；`R休` 與 `R*[R休]` 可 round-trip，需求月未標示 R* 的 R休會失敗。
- 非存在日期非空白、非法格值、M/T 欄位混用、T 能力／月班別錯誤會失敗。
- X 同日與跨午夜時間可讀寫；錯誤時區、日期或超過 24 小時會失敗。
- 非常態班型 CSV 可讀成 typed table；月班表中的非空白班型名稱或代碼會轉成同時間的 X，重複或非法定義會失敗。
- 八週區間非 56 日、缺口、重疊、假日在六日或區間外會得到 `InvalidInput`。

## 人員與歷史

- 既有人員有完整上月歷史時可建模。
- 本月前已到職卻缺上月歷史時得到 `InvalidInput`。
- 本月新進可無上月列；到職前日格必須空白，且不計出勤或班位。
- 新進人員的區間已計 R/R1 由到職前六日與國定假日推導。
- A/B 切換時，Opening 屬 A，Closing 屬 B，且期末累積只算到目標月底。

## 規則與最佳化

- 共用每日單一狀態、11 小時工作間隔、最多六日無 R、區間 16 R/R1 額度皆有可行與不可行案例。
- M/T 每人 R休精確數量只使用 R*，不計入 R/R1 月與區間額度；R休不重置七日 R 規則但會中斷實際工作連段。
- M 的站群、精確班位、外派站點、跨月夜–休–早／午與不跨月同班別區塊符合文件。
- M 早／午／夜班數公平只比較同站群整月人員，並以變異數等價量及 1:1:2 權重計分。
- T 的本月班別與延伸日輪轉作為 `NonMonthlyShift ×9` 軟規則基準；固定跨班是有效輸入，跨班人員計入實際工作班別的出勤、專業與能力。
- 每個具名違反量、權重與 Priority 字典序符合 `docs/05-soft-rules.md`。
- 候選差異只計目標月已到職、非固定格，且達 10% 門檻。
- 時間到期回傳 `TimeLimit`；呼叫端取消丟出 `OperationCanceledException`。

## CLI 與範例

- Redirected stdin 可完成五個互動答案，自動判斷 M/T，並產生月班表候選。
- 有候選 exit 0；輸入錯誤／無解／無候選 exit 1；取消 exit 130。
- `examples/m-2026-09` 可產生 `candidate-1.csv` 與外派檔。
- `examples/t-2026-09` 可產生 `candidate-1.csv`，且可在新進人員輸出驗證到職前額度。
