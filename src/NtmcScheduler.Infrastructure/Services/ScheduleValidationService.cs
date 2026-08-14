using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class ScheduleValidationService(NtmcDbContext db) : IScheduleValidationService
{
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);
    private static readonly string[] MStations =
    [
        "LB01", "LB02", "LB03", "LB04", "LB05", "LB06",
        "LB07", "LB08", "LB09", "LB10", "LB11", "LB12"
    ];

    public async Task<(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ScheduleEmployeeStats> Stats)> ValidateAsync(
        Guid versionId,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var version = await db.ScheduleVersions
            .AsSplitQuery()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .Include(x => x.ExternalAssignments)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        var previous = await PreviousAssignmentsAsync(version, cancellationToken);
        var issues = new List<ValidationIssue>();
        ValidateResolvedDailyAssignments(version, issues);
        ValidateMinimumRestGap(version, previous, issues);
        ValidateGeneralRestInEverySevenDays(version, previous, issues);
        ValidateEightWeekBalances(version, issues);
        if (version.Workspace == WorkspaceCode.M) ValidateM(version, issues);
        else ValidateT(version, issues);
        return (issues, CalculateStats(version));
    }

    private async Task<Dictionary<string, Dictionary<DateOnly, ScheduleAssignment>>> PreviousAssignmentsAsync(
        ScheduleVersion version,
        CancellationToken cancellationToken)
    {
        var adopted = await db.AdoptedSchedules.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Workspace == version.Workspace && x.Month == version.Month.AddMonths(-1), cancellationToken);
        if (adopted is null) return [];
        return await db.ScheduleEmployeeSnapshots.AsNoTracking()
            .Where(x => x.ScheduleVersionId == adopted.ScheduleVersionId)
            .Select(x => new { x.EmployeeCode, Assignments = x.Assignments.Where(a => a.Date >= version.Month.AddDays(-7)).ToList() })
            .ToDictionaryAsync(x => x.EmployeeCode, x => x.Assignments.ToDictionary(a => a.Date), cancellationToken);
    }

    private static void ValidateResolvedDailyAssignments(ScheduleVersion version, List<ValidationIssue> issues)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        foreach (var employee in version.Employees)
        {
            var byDate = employee.Assignments.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToArray());
            var leaveRest = employee.Assignments.Where(x => x.Date >= version.Month && x.Date <= monthEnd && x.Kind == "LeaveRest").ToArray();
            if (leaveRest.Length > employee.RequestedLeaveRestCount)
                issues.Add(new(ValidationSeverity.Error, "R休 上限", $"本月 R休 {leaveRest.Length} 日，超過上限 {employee.RequestedLeaveRestCount} 日。", employee.EmployeeCode));
            foreach (var cell in leaveRest.Where(x => !x.RequestedRest))
                issues.Add(new(ValidationSeverity.Error, "R休 指定日期", "R休 只能排在該員工的 R* 日期。", employee.EmployeeCode, cell.Date));
            for (var date = version.Month; date <= monthEnd; date = date.AddDays(1))
            {
                if (employee.EmploymentStartDate is { } start && date < start) continue;
                if (!byDate.TryGetValue(date, out var cells) || cells.Length != 1 || cells[0].Kind == "Unresolved")
                    issues.Add(new(ValidationSeverity.Error, "每日唯一指派", "每位在職員工每天必須有且只有一個已決定狀態。", employee.EmployeeCode, date));
            }
            foreach (var cell in employee.Assignments.Where(x => x.Kind == "WorkEvent"))
            {
                if (cell.EventStart is null || cell.EventEnd is null || cell.EventEnd <= cell.EventStart ||
                    cell.EventEnd - cell.EventStart > TimeSpan.FromHours(24) || cell.EventStart.Value.Offset != TaipeiOffset || cell.EventEnd.Value.Offset != TaipeiOffset)
                    issues.Add(new(ValidationSeverity.Error, "X 時間", "X 必須使用台北時間，結束晚於開始且長度不超過 24 小時。", employee.EmployeeCode, cell.Date));
                else if (DateOnly.FromDateTime(cell.EventStart.Value.DateTime) != cell.Date)
                    issues.Add(new(ValidationSeverity.Error, "X 歸屬日期", "X 必須歸在台北時間的開始日期。", employee.EmployeeCode, cell.Date));
            }
        }
    }

    private static void ValidateMinimumRestGap(
        ScheduleVersion version,
        IReadOnlyDictionary<string, Dictionary<DateOnly, ScheduleAssignment>> previous,
        List<ValidationIssue> issues)
    {
        foreach (var employee in version.Employees)
        {
            var cells = previous.GetValueOrDefault(employee.EmployeeCode)?.Values.Concat(employee.Assignments) ?? employee.Assignments;
            var work = cells.Select(cell => WorkInterval(version.Workspace, cell)).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x.Start).ToArray();
            for (var index = 1; index < work.Length; index++)
                if (work[index].Start - work[index - 1].End < TimeSpan.FromHours(11))
                    issues.Add(new(ValidationSeverity.Error, "最少十一小時休息", "相鄰工作區間重疊或休息少於十一小時。", employee.EmployeeCode, work[index].Date));
        }
    }

    private static void ValidateGeneralRestInEverySevenDays(
        ScheduleVersion version,
        IReadOnlyDictionary<string, Dictionary<DateOnly, ScheduleAssignment>> previous,
        List<ValidationIssue> issues)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        foreach (var employee in version.Employees)
        {
            var cells = new Dictionary<DateOnly, ScheduleAssignment>();
            foreach (var pair in previous.GetValueOrDefault(employee.EmployeeCode) ?? []) cells[pair.Key] = pair.Value;
            foreach (var cell in employee.Assignments) cells[cell.Date] = cell;
            for (var end = version.Month; end <= monthEnd; end = end.AddDays(1))
            {
                var start = end.AddDays(-6);
                if (employee.EmploymentStartDate is { } employmentStart && start < employmentStart) continue;
                if (Enumerable.Range(0, 7).Select(start.AddDays).All(date => cells.GetValueOrDefault(date)?.Kind != "Rest"))
                    issues.Add(new(ValidationSeverity.Error, "連續七日至少一日一般 R", "這個七日區間沒有一般 R；R1 與 R休不會重置本規則。", employee.EmployeeCode, end));
            }
        }
    }

    private static void ValidateEightWeekBalances(ScheduleVersion version, List<ValidationIssue> issues)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        foreach (var employee in version.Employees)
        foreach (var interval in version.ConfigurationRevision.RestIntervals.Where(x => x.Start <= monthEnd && x.End >= version.Month))
        {
            var start = interval.Start < version.Month ? version.Month : interval.Start;
            var end = interval.End > monthEnd ? monthEnd : interval.End;
            var cells = employee.Assignments.Where(x => x.Date >= start && x.Date <= end).ToArray();
            var rest = (interval.Start < version.Month ? employee.OpeningRest ?? 0 : 0) + cells.Count(x => x.Kind == "Rest");
            var special = (interval.Start < version.Month ? employee.OpeningSpecialRest ?? 0 : 0) + cells.Count(x => x.Kind == "SpecialRest");
            var requiredSpecial = interval.NationalHolidays.Count;
            if (rest > 16 || special > requiredSpecial || interval.End <= monthEnd && (rest != 16 || special != requiredSpecial))
                issues.Add(new(ValidationSeverity.Error, "八週 R/R1 額度", $"截至 {end:yyyy-MM-dd} 的區間累計為 R={rest}、R1={special}，不符合 R=16、R1={requiredSpecial}。", employee.EmployeeCode, end));
            else if (interval.End > monthEnd)
            {
                var remainingDays = interval.End.DayNumber - monthEnd.DayNumber;
                if (rest + remainingDays < 16 || special + remainingDays < requiredSpecial || rest + special + remainingDays < 16 + requiredSpecial)
                    issues.Add(new(ValidationSeverity.Error, "八週 R/R1 可完成性", $"截至 {monthEnd:yyyy-MM-dd} 的剩餘日數不足以完成 R=16、R1={requiredSpecial}。", employee.EmployeeCode, monthEnd));
            }
        }
    }

    private static void ValidateM(ScheduleVersion version, List<ValidationIssue> issues)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        foreach (var employee in version.Employees)
        foreach (var cell in employee.Assignments.Where(x => x.Kind == "Work"))
        {
            if (cell.Station is null || cell.Shift is null || !MStations.Contains(cell.Station) ||
                !StationsInSameGroup(employee.Affiliation).Contains(cell.Station))
                issues.Add(new(ValidationSeverity.Error, "M 合法站群", "正常班必須同時指定合法車站與班別，且車站須位於員工所屬三站群組。", employee.EmployeeCode, cell.Date, cell.Station, cell.Shift));
        }
        for (var date = version.Month; date <= monthEnd; date = date.AddDays(1))
        foreach (var station in MStations)
        foreach (var shift in new[] { "Early", "Afternoon", "Night" })
        {
            var internalCount = version.Employees.SelectMany(x => x.Assignments).Count(x => x.Date == date && x.Kind == "Work" && x.Station == station && x.Shift == shift);
            var externalCount = version.ExternalAssignments.Where(x => x.Date == date && x.Station == station && x.Shift == shift).Sum(x => x.Count);
            var actual = internalCount + externalCount;
            var required = shift is "Early" or "Afternoon" ? 1 : station is "LB01" or "LB06" or "LB08" or "LB12" ? 1 : 0;
            var multipleAllowed = station is "LB01" or "LB06" or "LB07" or "LB12" && shift is "Early" or "Afternoon";
            if (actual < required || !multipleAllowed && actual != required)
                issues.Add(new(ValidationSeverity.Error, "M 班位 Coverage", $"{station} {ShiftLabel(shift)}需求 {required} 人，目前 {actual} 人。", null, date, station, shift));
        }

        foreach (var employee in version.Employees)
        {
            var byDate = employee.Assignments.ToDictionary(x => x.Date);
            for (var date = version.Month.AddDays(1); date < monthEnd; date = date.AddDays(1))
            {
                if (byDate.GetValueOrDefault(date.AddDays(-1)) is { Kind: "Work", Shift: "Night" } &&
                    byDate.GetValueOrDefault(date) is { Kind: "Rest" or "SpecialRest" or "LeaveRest" } &&
                    byDate.GetValueOrDefault(date.AddDays(1)) is { Kind: "Work", Shift: "Early" or "Afternoon" } next)
                    issues.Add(new(ValidationSeverity.Warning, next.Shift == "Early" ? "夜休早" : "夜休小", "希望避免夜班、休假後立即接早／小班。", employee.EmployeeCode, date));
            }
        }
    }

    private static void ValidateT(ScheduleVersion version, List<ValidationIssue> issues)
    {
        foreach (var employee in version.Employees)
        {
            if (employee.Ability is < 1 or > 5 || employee.MonthlyShift is not ("Early" or "Afternoon" or "Night"))
                issues.Add(new(ValidationSeverity.Error, "T 人員欄位", "T 員工必須具備能力 1–5 與當月班別。", employee.EmployeeCode));
            foreach (var cell in employee.Assignments.Where(x => x.Kind == "Work" && x.Shift != employee.MonthlyShift))
                issues.Add(new(ValidationSeverity.Warning, "月班別不一致", $"實際 {ShiftLabel(cell.Shift)}不同於當月 {ShiftLabel(employee.MonthlyShift)}。", employee.EmployeeCode, cell.Date, null, cell.Shift));
        }
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        for (var date = version.Month; date <= monthEnd; date = date.AddDays(1))
        foreach (var shift in new[] { "Early", "Afternoon", "Night" })
        {
            var working = version.Employees.Where(employee => employee.Assignments.Any(x => x.Date == date && x.Kind == "Work" && x.Shift == shift)).ToArray();
            var expected = version.Employees.Count(x => x.MonthlyShift == shift);
            if (working.Length < Math.Max(0, expected - 2))
                issues.Add(new(ValidationSeverity.Warning, "班組出勤不足", $"{ShiftLabel(shift)}正常出勤 {working.Length} 人，低於月班組人數 {expected} 減 2。", null, date, null, shift));
            var missingSpecialties = version.Employees.Where(x => x.MonthlyShift == shift).Select(x => x.Affiliation).Distinct()
                .Except(working.Select(x => x.Affiliation)).ToArray();
            if (missingSpecialties.Length > 0)
                issues.Add(new(ValidationSeverity.Warning, "專業缺席", $"{ShiftLabel(shift)}缺少專業：{string.Join('、', missingSpecialties)}。", null, date, null, shift));
            var highAbility = working.Count(x => x.Ability >= 4);
            if (highAbility < 2)
                issues.Add(new(ValidationSeverity.Warning, "高能力人員不足", $"{ShiftLabel(shift)}能力 4–5 僅 {highAbility} 人。", null, date, null, shift));
        }
    }

    private static IReadOnlyList<ScheduleEmployeeStats> CalculateStats(ScheduleVersion version)
    {
        var holidays = version.ConfigurationRevision.RestIntervals.SelectMany(x => x.NationalHolidays).Select(x => x.Date).ToHashSet();
        return version.Employees.OrderBy(x => x.EmployeeCode).Select(employee =>
        {
            var work = employee.Assignments.Where(x => x.Kind is "Work" or "WorkEvent").ToArray();
            return new ScheduleEmployeeStats(
                employee.EmployeeCode,
                employee.Assignments.Count(x => x.Kind == "Rest"),
                employee.Assignments.Count(x => x.Kind == "SpecialRest"),
                employee.Assignments.Count(x => x.Kind == "LeaveRest"),
                work.Count(x => !IsHoliday(x.Date, holidays)),
                work.Count(x => IsHoliday(x.Date, holidays)),
                employee.Assignments.Count(x => x.Kind == "Work" && x.Shift == "Early"),
                employee.Assignments.Count(x => x.Kind == "Work" && x.Shift == "Afternoon"),
                employee.Assignments.Count(x => x.Kind == "Work" && x.Shift == "Night"),
                employee.Assignments.Count(x => x.Kind == "WorkEvent"));
        }).ToArray();
    }

    private static (DateOnly Date, DateTimeOffset Start, DateTimeOffset End)? WorkInterval(WorkspaceCode workspace, ScheduleAssignment cell)
    {
        if (cell.Kind == "WorkEvent" && cell.EventStart is not null && cell.EventEnd is not null)
            return (cell.Date, cell.EventStart.Value, cell.EventEnd.Value);
        if (cell.Kind != "Work" || cell.Shift is null) return null;
        var (start, end, nextDay) = (workspace, cell.Shift) switch
        {
            (WorkspaceCode.M, "Early") => (new TimeOnly(6, 30), new TimeOnly(14, 30), false),
            (WorkspaceCode.M, "Afternoon") => (new TimeOnly(14, 20), new TimeOnly(22, 20), false),
            (WorkspaceCode.M, "Night") => (new TimeOnly(22, 0), new TimeOnly(7, 0), true),
            (WorkspaceCode.T, "Early") => (new TimeOnly(7, 0), new TimeOnly(15, 0), false),
            (WorkspaceCode.T, "Afternoon") => (new TimeOnly(15, 0), new TimeOnly(23, 0), false),
            (WorkspaceCode.T, "Night") => (new TimeOnly(23, 0), new TimeOnly(7, 0), true),
            _ => throw new DomainValidationException("班表含有不支援的班別。")
        };
        return (cell.Date,
            new DateTimeOffset(cell.Date.ToDateTime(start), TaipeiOffset),
            new DateTimeOffset(cell.Date.AddDays(nextDay ? 1 : 0).ToDateTime(end), TaipeiOffset));
    }

    private static string[] StationsInSameGroup(string homeStation)
    {
        if (!MStations.Contains(homeStation)) return [];
        var first = ((int.Parse(homeStation[2..]) - 1) / 3) * 3;
        return MStations.Skip(first).Take(3).ToArray();
    }

    private static bool IsHoliday(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(date);

    private static string ShiftLabel(string? shift) => shift switch
    {
        "Early" => "早班",
        "Afternoon" => "午／小班",
        "Night" => "夜班",
        _ => shift ?? "未指定班別"
    };
}
