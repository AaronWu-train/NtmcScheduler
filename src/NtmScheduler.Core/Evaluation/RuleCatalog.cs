using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

/// <summary>
/// 規則目錄：所有規則的 ID、名稱、白話說明、優先層、預設順序、參數說明的單一來源。
/// 修改軟規則順序或新增規則參數時，先改這裡，再改對應的評估器／Solver 編碼。
/// </summary>
public static class RuleCatalog
{
    public enum Layer
    {
        /// <summary>P0 硬規則，不可關閉、不可調序。</summary>
        Hard = 0,
        /// <summary>P1 最高順位軟規則（目前僅 GEN-R-01），不可關閉、不可調序。</summary>
        SoftP1 = 1,
        SoftP2 = 2,
        SoftP3 = 3,
        SoftP4 = 4
    }

    public sealed record RuleInfo(
        string RuleId,
        string DisplayName,
        string Description,
        Layer Layer,
        Unit? UnitOnly,
        string ViolationDefinition,
        string? ParametersHelp = null);

    /// <summary>全部規則（含硬規則）。</summary>
    public static readonly IReadOnlyList<RuleInfo> All =
    [
        // Hard — general
        new("GEN-H-01", "每日唯一狀態",
            "每人每天只能有一個狀態。若該日是 X 的開始日，格上必須顯示 X，不能再排其他班或休假。",
            Layer.Hard, null, "缺狀態或 X 開始日重疊其他狀態的人×日數"),
        new("GEN-H-02", "連續工作上限",
            "任意兩次一般休假（R 或已滿足的 R*）之間，工作日（早／午／夜／X）最多 6 天。R1 不算一般休假，不會重置這個計數。",
            Layer.Hard, null, "計數超過 6 的人×日數"),
        new("GEN-H-03", "休息至少 11 小時",
            "相鄰兩段實際工作（含正常班與 X）之間，休息時間至少 11 小時。",
            Layer.Hard, null, "休息不足 11 小時的相鄰工作對數"),
        new("GEN-H-04", "8 週休假額度",
            "每個 8 週週期：一般休假（R＋R*）與國定假日休假（R1）分開計算。週期結束時兩者都必須剛好達標；尚未結束時不得超額，且剩餘天數須足以補足。跨月時還要預留一部分一般 R 給下個月。",
            Layer.Hard, null, "額度不符的人×週期數（含比例保留違規）"),
        new("GEN-H-05", "歷史與固定事件不可改寫",
            "已保存的歷史班表與 X 事件，求解器不得改寫。由輸入驗證與模型固定常數強制。",
            Layer.Hard, null, "（評估完成班表時固定為 0；違規在輸入驗證階段攔截）"),

        // Hard — M
        new("M-H-01", "同群組工作",
            "站務人員只能在本人所屬車站或同群組的其他車站上班，不可跨群組。",
            Layer.Hard, Unit.M, "跨群組工作的人×日數"),
        new("M-H-02", "班位必須補足",
            "每個車站每個班別，必須剛好由一名內部人員或合法外派補足。缺班分析方案才允許空缺。",
            Layer.Hard, Unit.M, "缺額班位數"),
        new("M-H-03", "外派站限制",
            "只有 LB02、LB04、LB11 可以使用外派補足；其他車站不行。",
            Layer.Hard, Unit.M, "非法外派班位數"),

        // Hard — T
        new("T-H-01", "固定班別",
            "檢測人員的正常班只能是當月固定班組（早／午／夜）。延伸日優先用下月班組，否則依早→午→夜→早輪轉推算。",
            Layer.Hard, Unit.T, "班別不符的人×日數"),

        // Soft — shared
        new("GEN-R-01", "滿足指定休假 R*",
            "盡量滿足使用者指定的休假日。若系統滿足，該日顯示為 R*（效果與 R 相同）。",
            Layer.SoftP1, null, "未滿足的 R* 人×日數"),
        new("GEN-S-STREAK", "連續工作長度",
            "希望連續工作區段長度接近 3～5 天。太短或太長都會累加偏離量。",
            Layer.SoftP3, null, "已結束區段的 D(L) 總和；D(L)=max(0,3−L)+max(0,L−5)"),
        new("GEN-S-WEEKDAY-R", "平日休假公平",
            "同儕群組（M＝同車站群組；T＝同月班組）在平日的 R／R* 次數盡量接近。",
            Layer.SoftP4, null, "各相交週期的 max−min 加總"),
        new("GEN-S-WEEKEND-R", "週末休假公平",
            "同儕群組在週末（六、日）的 R／R* 次數盡量接近。",
            Layer.SoftP4, null, "各相交週期的 max−min 加總"),

        // Soft — M
        new("M-S-EXT", "少用外派",
            "盡量不要用外派補班位。",
            Layer.SoftP2, Unit.M, "外派班位數"),
        new("M-S-HOME", "優先本站",
            "正常班盡量排在本人所屬車站，不要跨站支援。",
            Layer.SoftP2, Unit.M, "非本站正常班日數"),
        new("M-S-BLOCK", "同班別連續次數",
            "同一人連續排同一班別（早／午／夜）的次數，希望接近 3～5。",
            Layer.SoftP3, Unit.M, "已結束同班別區塊的 D(次數) 總和"),
        new("M-S-NIGHT-EARLY", "避免夜→休→早",
            "避免「夜班 → R／R* → 早班」這種組合（中間若是 R1 不算）。",
            Layer.SoftP3, Unit.M, "出現次數"),
        new("M-S-NIGHT-AFTERNOON", "避免夜→休→午",
            "避免「夜班 → R／R* → 午班」這種組合（中間若是 R1 不算）。",
            Layer.SoftP3, Unit.M, "出現次數"),
        new("M-S-RESTSWITCH", "換班經過休假",
            "相鄰兩次有效正常班若班別不同，中間最好有 R 或 R*（R1／X 不算）。",
            Layer.SoftP3, Unit.M, "未經過 R／R* 的換班次數"),
        new("M-S-ROTATE", "換班方向",
            "換班時優先依 早→午→夜→早 的方向。",
            Layer.SoftP3, Unit.M, "非優先方向的換班次數"),
        new("M-S-SUPPORT-FAIR", "跨站支援公平",
            "同群組內，每人跨站支援的天數盡量接近。",
            Layer.SoftP4, Unit.M, "各相交週期的 max−min 加總"),

        // Soft — T
        new("T-S-ATTEND", "每班一半出勤",
            "每個班組每天正常出勤人數，盡量達到該班組人數的一半（無條件捨去）。",
            Layer.SoftP2, Unit.T, "Σ max(0, floor(人數/2) − 出勤)"),
        new("T-S-SPECIALTY", "專業分組出勤",
            "每個班組內，每個非空白專業，每天至少要有一人正常出勤。",
            Layer.SoftP2, Unit.T, "缺席的（班×日×專業）數"),
        new("T-S-ABILITY", "平均能力至少 3",
            "每個班組每天出勤人員的平均能力值至少要到 3。",
            Layer.SoftP2, Unit.T, "Σ max(0, 3×出勤人數 − 能力總和)"),
        new("T-S-MONTH-REST", "夜轉早休假",
            "從夜班組轉到早班組的人，最後一夜與第一次早之間，至少要有 2 天 R／R*。",
            Layer.SoftP3, Unit.T, "Σ max(0, 2 − 中間 R/R* 數)"),
        new("T-S-MONTH-BALANCE", "月底月初休假分散",
            "夜轉早的人，前月最後一天休假人數與本月 1 日休假人數差越小越好。",
            Layer.SoftP3, Unit.T, "|前月末休假人數 − 本月初休假人數|"),
    ];

    /// <summary>M 軟規則預設求解順序（只含軟規則，已啟用）。</summary>
    public static readonly IReadOnlyList<(string RuleId, int Order)> DefaultMSoftOrder =
    [
        ("GEN-R-01", 1),
        ("M-S-EXT", 2),
        ("M-S-HOME", 3),
        ("GEN-S-STREAK", 4),
        ("M-S-BLOCK", 5),
        ("M-S-NIGHT-EARLY", 6),
        ("M-S-NIGHT-AFTERNOON", 7),
        ("M-S-RESTSWITCH", 8),
        ("M-S-ROTATE", 9),
        ("GEN-S-WEEKDAY-R", 10),
        ("GEN-S-WEEKEND-R", 11),
        ("M-S-SUPPORT-FAIR", 12),
    ];

    /// <summary>T 軟規則預設求解順序（只含軟規則，已啟用）。含 GEN-S-STREAK。</summary>
    public static readonly IReadOnlyList<(string RuleId, int Order)> DefaultTSoftOrder =
    [
        ("GEN-R-01", 1),
        ("T-S-ATTEND", 2),
        ("T-S-SPECIALTY", 3),
        ("T-S-ABILITY", 4),
        ("GEN-S-STREAK", 5),
        ("T-S-MONTH-REST", 6),
        ("T-S-MONTH-BALANCE", 7),
        ("GEN-S-WEEKDAY-R", 8),
        ("GEN-S-WEEKEND-R", 9),
    ];

    public static RuleInfo? Find(string ruleId) =>
        All.FirstOrDefault(r => r.RuleId == ruleId);

    public static string DisplayNameOf(string ruleId) =>
        Find(ruleId)?.DisplayName ?? ruleId;

    public static string DescribeViolation(string ruleId, string message) =>
        $"{DisplayNameOf(ruleId)}（{ruleId}）：{message}";

    public static IReadOnlyList<RuleInfo> SoftRulesFor(Unit unit) =>
        All.Where(r => r.Layer != Layer.Hard
                       && (r.UnitOnly is null || r.UnitOnly == unit)).ToList();

    public static IReadOnlyList<RuleInfo> HardRulesFor(Unit unit) =>
        All.Where(r => r.Layer == Layer.Hard
                       && (r.UnitOnly is null || r.UnitOnly == unit)).ToList();

    public static IReadOnlyList<(string RuleId, int Order)> DefaultSoftOrder(Unit unit) =>
        unit == Unit.M ? DefaultMSoftOrder : DefaultTSoftOrder;

    /// <summary>建立某單位的完整預設規則列（硬＋軟），供資料庫種子使用。</summary>
    public static IReadOnlyList<(string RuleId, int Priority, int Order, bool Enabled)> DefaultRows(Unit unit)
    {
        var rows = new List<(string, int, int, bool)>();
        var order = 0;
        foreach (var hard in HardRulesFor(unit))
            rows.Add((hard.RuleId, 0, order++, true));

        foreach (var (ruleId, softOrder) in DefaultSoftOrder(unit))
        {
            var info = Find(ruleId) ?? throw new InvalidOperationException($"目錄缺少 {ruleId}");
            rows.Add((ruleId, (int)info.Layer, softOrder, true));
        }

        return rows;
    }
}
