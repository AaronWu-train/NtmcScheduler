using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class ScheduleValidationService(IDbContextFactory<NtmcDbContext> dbFactory) : IScheduleValidationService
{
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    public async Task<(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ScheduleEmployeeStats> Stats)> ValidateAsync(
        Guid versionId,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await ValidateAsync(db, versionId, cancellationToken);
    }

    internal async Task<(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ScheduleEmployeeStats> Stats)> ValidateAsync(
        NtmcDbContext db,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var version = await db.ScheduleVersions
            .AsSplitQuery()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.StandardShiftTimes)
            .Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .Include(x => x.ExternalAssignments)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        var previous = await PreviousAssignmentsAsync(db, version, cancellationToken);
        var shiftTimes = SolverScheduleMapper.ToStandardShiftTimes(version.ConfigurationRevision, version.Workspace);
        var issues = new List<ValidationIssue>();
        ValidateResolvedDailyAssignments(version, issues);
        ValidateMinimumRestGap(version, previous, shiftTimes, issues);
        ValidateGeneralRestInEverySevenDays(version, previous, issues);
        ValidateEightWeekBalances(version, issues);
        if (version.Workspace.IsStation()) ValidateM(version, MonthlySettings(version), issues);
        else ValidateT(version, issues);
        return (issues, CalculateStats(version));
    }

    private static async Task<Dictionary<string, Dictionary<DateOnly, ScheduleAssignment>>> PreviousAssignmentsAsync(
        NtmcDbContext db,
        ScheduleVersion version,
        CancellationToken cancellationToken)
    {
        if (version.SourceRunId is { } runId)
        {
            var snapshot = await db.ScheduleRuns.AsNoTracking().Where(x => x.Id == runId)
                .Select(x => x.InputSnapshotJson).SingleOrDefaultAsync(cancellationToken)
                ?? throw new DomainValidationException("找不到班表版本的求解輸入快照。");
            var input = JsonSerializer.Deserialize<ScheduleInput>(snapshot, ServiceSupport.JsonOptions)
                ?? throw new DomainValidationException("班表版本的求解輸入快照無法讀取。");
            return input.PreviousMonth.Employees.ToDictionary(
                x => x.EmployeeId,
                x => x.Assignments.ToDictionary(pair => pair.Key, pair => new ScheduleAssignment
                {
                    Date = pair.Key,
                    Kind = pair.Value.Kind?.ToString() ?? "Unresolved",
                    RequestedRest = pair.Value.RequestedRest,
                    Station = pair.Value.Station,
                    Shift = pair.Value.Shift?.ToString(),
                    EventStart = pair.Value.EventStart,
                    EventEnd = pair.Value.EventEnd
                }),
                StringComparer.Ordinal);
        }
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
        StandardShiftTimes shiftTimes,
        List<ValidationIssue> issues)
    {
        foreach (var employee in version.Employees)
        {
            var cells = previous.GetValueOrDefault(employee.EmployeeCode)?.Values.Concat(employee.Assignments) ?? employee.Assignments;
            var work = cells.Select(cell => WorkInterval(version.Workspace, shiftTimes, cell)).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x.Start).ToArray();
            for (var index = 1; index < work.Length; index++)
                if (work[index].Start - work[index - 1].End < TimeSpan.FromHours(11))
                    issues.Add(new(ValidationSeverity.Error, "最少十一小時休息", "相鄰工作區間重疊或休息少於十一小時。", employee.EmployeeCode, work[index].Date, IsLaborLawViolation: true));
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
                var window = Enumerable.Range(0, 7).Select(start.AddDays).ToArray();
                if (window.All(cells.ContainsKey) && window.All(date => cells[date].Kind != "Rest"))
                    issues.Add(new(ValidationSeverity.Error, "連續七日至少一日一般 R", "這個七日區間沒有一般 R；R1 與 R休不會重置本規則。", employee.EmployeeCode, end, IsLaborLawViolation: true));
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

    private static void ValidateM(ScheduleVersion version, MonthlySchedulingSettings settings, List<ValidationIssue> issues)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        foreach (var employee in version.Employees)
            foreach (var cell in employee.Assignments.Where(x => x.Kind == "Work"))
            {
                if (cell.Station is null || cell.Shift is null || !settings.MStations.Any(x => x.Code == cell.Station) ||
                    !StationsInSameGroup(settings, employee.Affiliation).Contains(cell.Station))
                    issues.Add(new(ValidationSeverity.Error, "M 合法站群", "正常班必須同時指定合法車站與班別，且車站須位於員工所屬群組。", employee.EmployeeCode, cell.Date, cell.Station, cell.Shift));
            }
        foreach (var external in version.ExternalAssignments.Where(x => settings.MStations.All(station => station.Code != x.Station)))
            issues.Add(new(ValidationSeverity.Error, "站務外援站碼", $"外援車站必須為 {settings.MStations.First().Code}–{settings.MStations.Last().Code}。", null, external.Date, external.Station, external.Shift));
        for (var date = version.Month; date <= monthEnd; date = date.AddDays(1))
            foreach (var station in settings.MStations)
                foreach (var shift in new[] { "Early", "Afternoon", "Night" })
                {
                    var range = shift switch { "Early" => station.Early, "Afternoon" => station.Afternoon, _ => station.Night };
                    var internalCount = version.Employees.SelectMany(x => x.Assignments)
                        .Count(x => x.Date == date && x.Kind == "Work" && x.Station == station.Code && x.Shift == shift);
                    var externalCount = version.ExternalAssignments.Where(x => x.Date == date && x.Station == station.Code && x.Shift == shift).Sum(x => x.Count);
                    var total = internalCount + externalCount;
                    if (total < range.Minimum || total > range.Maximum)
                        issues.Add(new(ValidationSeverity.Error, "M 班位人數", $"內部與外援合計 {total} 人，必須介於 {range.Minimum}–{range.Maximum} 人。", null, date, station.Code, shift));
                    var expectedExternal = station.ExternalSupport == ExternalSupportLevel.Disallowed ? 0 : Math.Max(0, range.Minimum - internalCount);
                    if (externalCount != expectedExternal)
                        issues.Add(new(ValidationSeverity.Error, "M 外援使用", $"此站班外援應為補足最低需求所需的 {expectedExternal} 人，目前為 {externalCount} 人。", null, date, station.Code, shift));
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
                var minimumAttendance = expected / 2;
                if (working.Length < minimumAttendance)
                    issues.Add(new(ValidationSeverity.Warning, "班組出勤不足", $"{ShiftLabel(shift)}正常出勤 {working.Length} 人，低於當日在職組員數 {expected} 人的一半 {minimumAttendance} 人。", null, date, null, shift));
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

    private static (DateOnly Date, DateTimeOffset Start, DateTimeOffset End)? WorkInterval(WorkspaceCode workspace, StandardShiftTimes shiftTimes, ScheduleAssignment cell)
    {
        if (cell.Kind == "WorkEvent" && cell.EventStart is not null && cell.EventEnd is not null)
            return (cell.Date, cell.EventStart.Value, cell.EventEnd.Value);
        if (cell.Kind != "Work" || cell.Shift is null) return null;
        if (!Enum.TryParse<Shift>(cell.Shift, out var shift))
            throw new DomainValidationException("班表含有不支援的班別。");
        var times = workspace.IsStation() ? shiftTimes.M : shiftTimes.T;
        var (start, end) = times.Resolve(cell.Date, shift);
        return (cell.Date, start, end);
    }

    private static MonthlySchedulingSettings MonthlySettings(ScheduleVersion version) =>
        string.IsNullOrWhiteSpace(version.MonthlySettingsJson)
            ? SolverScheduleMapper.DefaultMonthlySettings(version.Workspace, version.Month, SolverScheduleMapper.ToRestIntervals(version.ConfigurationRevision), version.Employees.Count)
            : JsonSerializer.Deserialize<MonthlySchedulingSettings>(version.MonthlySettingsJson, ServiceSupport.JsonOptions)
                ?? SolverScheduleMapper.DefaultMonthlySettings(version.Workspace, version.Month, SolverScheduleMapper.ToRestIntervals(version.ConfigurationRevision), version.Employees.Count);

    private static string[] StationsInSameGroup(MonthlySchedulingSettings settings, string homeStation)
    {
        var group = settings.MStations.FirstOrDefault(x => x.Code == homeStation)?.Group;
        return group is null ? [] : settings.MStations.Where(x => x.Group == group).Select(x => x.Code).ToArray();
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
