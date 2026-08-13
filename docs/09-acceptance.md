# 系統驗收

## Web 身分與授權

- 不存在註冊、忘記密碼與電子郵件重設路徑；管理者 CLI 建立首位管理者，管理頁建立其餘帳號與一次性密碼。
- 首次登入強制修改至少 8 字元、至少 2 種不同字元且包含數字的密碼；五次失敗鎖定 15 分鐘，另以帳號與 IP 做登入限流，錯誤訊息不揭露帳號是否存在。正式上線前須重新檢視並提高密碼政策。
- Viewer、M Editor、T Editor、同時 M/T Editor 與 Administrator 的頁面及 application service 寫入授權一致；猜測資源 GUID 不可越權修改。
- 停用、重設密碼及權限異動更新 security stamp；不提供持久登入，Cookie 為 Secure、HttpOnly、SameSite=Strict，閒置 30 分鐘到期。

## 共同設定、人員與 Demand

- 共同設定各區間恰為 56 日且連續；假日合法，儲存建立不可變版本，current pointer 使用 revision token，舊 Demand／班表仍可讀取原快照。
- M/T 員工主檔可新增、修改與封存；M 僅接受 LB01–LB12，T 能力為 1–5，既有月份不因主檔異動而改變。
- 每單位月份僅一份 Demand 草稿；重開仍保留，所有寫入以 revision token 拒絕陳舊更新。
- 建立 Demand 自動使用上月 `★`；不存在時求解前必須成功上傳 previous schedule。求解建立 immutable JSON input snapshot、hash、seed、程式版本與人員月快照。
- 背景佇列一次只執行一個 solver；重啟將 Queued／Running 安全回復為 Queued，終態正確區分 Optimal、TimeLimit、Infeasible、InvalidInput、Failed。

## 班表版本與編輯器

- 同單位同月可有多份班表，以 `AdoptedSchedule` 主鍵保證最多一份 `★`；紅色錯誤阻擋標星、黃色警示不阻擋。
- `★` 不可直接封存；改選後可封存。封存不刪除 assignments、快照與 audit。
- R、R1、R休、R* 契約、M/T 正常班與 X 可編輯；X 使用台北時間、歸開始日、可跨午夜且不超過 24 小時。
- 編輯後立即重算整月硬規則、Coverage、文件既有軟警示與每人統計；錯誤／警示都有圖示、文字、員工及日期定位，不只使用顏色。
- M 底部依站、日期顯示內部早／小／夜及外派；T 顯示正常出勤、專業涵蓋與能力 4–5 人數。

## 稽核、Log 與部署

- 登入、權限、設定、人員、Demand、匯入、求解、逐格修改、標星、封存與下載均寫入 AuditLog；成功資料異動與 AuditLog 同 transaction。
- AuditLog 有 UTC、actor snapshot、before/after、IP、User-Agent、CorrelationId，且不含密碼、Cookie、token、連線字串或 CSV 原文；應用程式沒有更新／刪除 AuditLog 的路徑。
- 所有路由頁面右上有鍵盤可操作 `?`；上傳前顯示 UTF-8/BOM、5 MB、固定表頭、日期時間、合法值及僅接受 CSV。
- SQLite 與 SQL Server 都能產生 migration SQL；Linux 測試環境需另完成 migration、備份／還原、持久化且加密的 Data Protection key ring、反向代理可信清單與一年 Log 保留驗收。

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
- M 萬年班表 CSV 可讀取 56 日空白／R／站班模板，空白日不加入 hint；重複代號、欄數、非空白非法格值、引用與站群錯誤會失敗。月班表的新模板代號可 round-trip，舊格式仍可讀取，留白時承接上月值。

## 人員與歷史

- 既有人員有完整上月歷史時可建模。
- 本月前已到職卻缺上月歷史時得到 `InvalidInput`。
- 本月新進可無上月列；到職前日格必須空白，且不計出勤或班位。
- 新進人員的區間已計 R/R1 由到職前六日與國定假日推導。
- A/B 切換時，Opening 屬 A，Closing 屬 B，且期末累積只算到目標月底。

## 規則與最佳化

- 共用每日單一狀態、11 小時工作間隔、最多六日無 R、區間 16 R/R1 額度皆有可行與不可行案例。
- M/T 每人 R休不超過輸入上限且只使用 R*；未使用數量以權重 1 納入 J1，指定休假違反量權重為 3。R休不計入 R/R1 月與區間額度、不重置七日 R 規則，但會中斷實際工作連段。
- M 的站群、班位人數、零需求班位禁排、外派站點與跨月夜–休–早／午符合文件；LB09 早／午可外派且前 4 人次免罰，原三站共用 70 人次免罰。`LB01`、`LB06`、`LB07`、`LB12` 的早、午班可多人且無上限；其餘站早、午班各至多 1 人。夜班一律至多 1 人，有需求時恰好 1 人。MS03 沿用 CS04 工作區段邊界，區段內出現超過一種正常班型時計 +1。
- M 萬年班表第 1 日對應 56 日 16R 區間首日；模板非空白格只以 `AddHint` 引導 hard-only 初始搜尋，空白格不 hint，固定格與 R* 優先，衝突 hint 不會使可行模型變成無解。初始可行解改寫為 incumbent hint 後才進行 J1 與直接加權合併的 `J4+J5`。
- M 的假日休假公平只比較同站群整月人員；M/T 每群皆以實際假日休假總數除以組內人數取得精確平均，每人在平均正負 1.5 天內不罰，超出後按離免罰區間的整數天數線性計罰。M 平日休假公平不建模；T 平日公平仍為同月班別最大差。早小班每人差距超過 4 才計罰；夜班以每人 3–4 天為零罰分目標，並檢查 0–8 天的罰分依序為 10、5、1、0、0、4、8、12、16。M 三項權重為假日休假 5、早小差距 2、夜班目標 50。
- M 先最佳化 J1，再最佳化直接加權合併的 `J4+J5`；非偏好輪轉、非所屬站指派與跨站支援公平暫停建模及輸出，其餘項目與權重符合 `docs/05-soft-rules.md`。
- T 的本月班別與延伸日輪轉作為月班別不一致（`NonMonthlyShift`）×9 的基準；固定跨班是有效輸入，跨班人員計入實際工作班別的出勤、專業與能力。每班能力 4–5 人員至少 2 位時，高能力人員不足（`Ability`）為 0、只有 1 位時為 1、完全沒有時為 10。
- 每個具名違反量、權重與 Priority 字典序符合 `docs/05-soft-rules.md`。
- 候選差異只計目標月已到職、非固定格，且每兩份達 5% 門檻。M 先像 T 一樣搜尋具名目標完全同分的第二份；第一次同分搜尋無結果才允許每項最多差 20%。M/T 都不預留固定候選時間，只使用目標最佳化後的剩餘總時限；M 進入候選搜尋後才保留當時餘時的一半給 20% fallback。T 替代候選維持 J1–J5 同分，不套用放寬。
- 時間到期回傳 `TimeLimit`；呼叫端取消丟出 `OperationCanceledException`。
- M/T CLI 的 `--search workers=N,seconds=N` 可調整 worker 數與整體總時限。M 可選 `seeds=N`，省略開關時為 4 workers、2 seeds、300 秒，CLI 採用第一候選具名目標字典序較佳的整批結果；T 為 8 workers、300 秒、seed 0，`seeds>1` 會失敗。CLI 顯示實際求解時間；非正整數、未知、缺漏或重複欄位會失敗。

## CLI 與範例

- Redirected stdin 可完成 T 的五個、M 的六個互動答案，自動判斷 M/T，並產生月班表候選。
- 有候選 exit 0；輸入錯誤／無解／無候選 exit 1；取消 exit 130。
- `examples/m-2026-09` 可產生 `candidate-1.csv` 與外派檔。
- `examples/t-2026-09` 可產生 `candidate-1.csv`，且可在新進人員輸出驗證到職前額度。
