using System.Text.Json;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;
using SolverAssignmentKind = NtmcScheduler.Solvers.AssignmentKind;
using SolverShift = NtmcScheduler.Solvers.Shift;

namespace NtmcScheduler.Infrastructure.Services;

internal static class SolverScheduleMapper
{
    public static MonthlySchedulingSettings ToMonthlySettings(DemandDraft demand)
    {
        var intervals = ToRestIntervals(demand.ConfigurationRevision);
        var defaults = DefaultMonthlySettings(demand.Workspace, demand.Month, intervals, demand.Employees.Count);
        var defaultStations = defaults.MStations;
        var stations = string.IsNullOrWhiteSpace(demand.MStationSettingsJson)
            ? defaultStations
            : JsonSerializer.Deserialize<MStationSetting[]>(demand.MStationSettingsJson, ServiceSupport.JsonOptions) ?? defaultStations;
        return new MonthlySchedulingSettings(
            demand.GeneralRestTarget ?? defaults.GeneralRestTarget,
            demand.SpecialRestTarget ?? defaults.SpecialRestTarget,
            stations);
    }

    public static MonthlySchedulingSettings DefaultMonthlySettings(
        WorkspaceCode workspace,
        DateOnly month,
        IReadOnlyList<RestInterval> intervals,
        int employeeCount)
    {
        var defaults = MonthlySchedulingDefaults.Create(month, intervals, employeeCount);
        return workspace == WorkspaceCode.YM
            ? defaults with
            {
                MStations = WorkspaceCodes.YmStations.Select((code, index) => new MStationSetting(
                    code,
                    $"G{index switch { <= 2 => 1, <= 5 => 2, <= 8 => 3, 9 => 4, <= 11 => 5, _ => 6 }}",
                    ExternalSupportLevel.Disallowed,
                    new(1, 1), new(1, 1), new(1, 1))).ToArray()
            }
            : defaults;
    }

    public static MonthlySchedulingSettingsDto ToDto(DemandDraft demand)
    {
        var settings = ToMonthlySettings(demand);
        var (rMin, rMax) = TargetBounds(demand, false);
        var (r1Min, r1Max) = TargetBounds(demand, true);
        return new(settings.GeneralRestTarget, settings.SpecialRestTarget, rMin, rMax, r1Min, r1Max,
            settings.MStations.Select(x => new MStationSettingDto(x.Code, x.Group, (ExternalSupportPolicy)x.ExternalSupport,
                new(x.Early.Minimum, x.Early.Maximum), new(x.Afternoon.Minimum, x.Afternoon.Maximum), new(x.Night.Minimum, x.Night.Maximum))).ToArray());
    }

    private static (int Minimum, int Maximum) TargetBounds(DemandDraft demand, bool special)
    {
        var monthEnd = demand.Month.AddMonths(1).AddDays(-1);
        var intervals = demand.ConfigurationRevision.RestIntervals.Where(x => x.Start <= monthEnd && x.End >= demand.Month).ToArray();
        if (demand.Employees.Count == 0) return (0, DateTime.DaysInMonth(demand.Month.Year, demand.Month.Month));
        var valid = Enumerable.Range(0, DateTime.DaysInMonth(demand.Month.Year, demand.Month.Month) + 1).Where(baseline =>
            demand.Employees.All(employee =>
            {
                var start = employee.EmploymentStartDate is { } hired && hired > demand.Month ? hired : demand.Month;
                var deduction = Enumerable.Range(0, Math.Max(0, start.DayNumber - demand.Month.DayNumber))
                    .Select(demand.Month.AddDays).Count(date => special
                        ? intervals.SelectMany(x => x.NationalHolidays).Any(x => x.Date == date)
                        : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
                var target = Math.Max(0, baseline - deduction);
                var minimum = 0;
                var maximum = 0;
                foreach (var interval in intervals)
                {
                    var activeStart = start > interval.Start ? start : interval.Start;
                    var segmentEnd = monthEnd < interval.End ? monthEnd : interval.End;
                    if (activeStart > segmentEnd) continue;
                    var segmentDays = segmentEnd.DayNumber - activeStart.DayNumber + 1;
                    var prior = interval.Start < demand.Month
                        ? special ? employee.OpeningSpecialRest ?? 0 : employee.OpeningRest ?? 0
                        : 0;
                    var quota = special ? interval.NationalHolidays.Count : 16;
                    var futureStart = monthEnd.AddDays(1) > activeStart ? monthEnd.AddDays(1) : activeStart;
                    var futureDays = futureStart <= interval.End ? interval.End.DayNumber - futureStart.DayNumber + 1 : 0;
                    minimum += Math.Max(0, quota - prior - futureDays);
                    maximum += Math.Min(segmentDays, Math.Max(0, quota - prior));
                }
                return target >= minimum && target <= maximum;
            })).ToArray();
        return valid.Length == 0 ? (0, 0) : (valid[0], valid[^1]);
    }

    public static MonthlySchedule ToMonthlySchedule(DemandDraft demand) => new(
        demand.Month,
        demand.Employees.OrderBy(x => x.EmployeeCode).Select(employee => new EmployeeMonthlySchedule
        {
            EmployeeId = employee.EmployeeCode,
            Name = employee.Name,
            Affiliation = employee.Affiliation,
            EmploymentStartDate = employee.EmploymentStartDate,
            Ability = employee.Ability,
            MonthlyShift = ParseShift(employee.MonthlyShift),
            PerpetualScheduleId = employee.PerpetualScheduleId,
            RequestedLeaveRestCount = employee.RequestedLeaveRestCount,
            OpeningUsage = employee.OpeningRest is null || employee.OpeningSpecialRest is null
                ? null
                : new RestUsage(employee.OpeningRest.Value, employee.OpeningSpecialRest.Value),
            Assignments = employee.Assignments.ToDictionary(x => x.Date, ToScheduleCell),
            ClosingUsage = null,
            NormalWorkCount = null
        }).ToArray());

    public static MonthlySchedule ToMonthlySchedule(ScheduleVersion version) => new(
        version.Month,
        version.Employees.OrderBy(x => x.EmployeeCode).Select(employee => new EmployeeMonthlySchedule
        {
            EmployeeId = employee.EmployeeCode,
            Name = employee.Name,
            Affiliation = employee.Affiliation,
            EmploymentStartDate = employee.EmploymentStartDate,
            Ability = employee.Ability,
            MonthlyShift = ParseShift(employee.MonthlyShift),
            PerpetualScheduleId = employee.PerpetualScheduleId,
            RequestedLeaveRestCount = null,
            OpeningUsage = employee.OpeningRest is null || employee.OpeningSpecialRest is null
                ? null
                : new RestUsage(employee.OpeningRest.Value, employee.OpeningSpecialRest.Value),
            Assignments = employee.Assignments.ToDictionary(x => x.Date, ToScheduleCell),
            ClosingUsage = employee.ClosingRest is null || employee.ClosingSpecialRest is null
                ? null
                : new RestUsage(employee.ClosingRest.Value, employee.ClosingSpecialRest.Value),
            NormalWorkCount = employee.NormalWorkCount
        }).ToArray());

    public static IReadOnlyList<RestInterval> ToRestIntervals(ConfigurationRevision revision) =>
        revision.RestIntervals.OrderBy(x => x.Start)
            .Select(x => new RestInterval(x.Start, x.End, x.NationalHolidays.Select(h => h.Date).ToHashSet()))
            .ToArray();

    public static NonStandardShiftTable ToNonStandardShifts(ConfigurationRevision revision) => new(
        revision.NonStandardShifts.Select(x => new NonStandardShift(x.Name, x.StartTime, x.EndTime, x.Code)).ToArray());

    public static StandardShiftTimes ToStandardShiftTimes(ConfigurationRevision revision, WorkspaceCode workspace) => new(
        ToWorkspaceShiftTimes(revision, workspace.IsStation() ? workspace.ToString() : "M", WorkspaceShiftTimes.DefaultM),
        ToWorkspaceShiftTimes(revision, "T", WorkspaceShiftTimes.DefaultT));

    private static WorkspaceShiftTimes ToWorkspaceShiftTimes(ConfigurationRevision revision, string workspace, WorkspaceShiftTimes defaults)
    {
        ShiftTimePair Pair(string shift, ShiftTimePair fallback)
        {
            var e = revision.StandardShiftTimes.FirstOrDefault(x => x.Workspace == workspace && x.Shift == shift);
            return e is null ? fallback : new ShiftTimePair(e.StartTime, e.EndTime);
        }
        return new WorkspaceShiftTimes(Pair("Early", defaults.Early), Pair("Afternoon", defaults.Afternoon), Pair("Night", defaults.Night));
    }

    public static ScheduleVersion ToVersion(
        MonthlySchedule schedule,
        WorkspaceCode workspace,
        Guid runId,
        int candidateIndex,
        ScheduleRunStatus sourceStatus,
        Guid configurationRevisionId,
        Guid actorId,
        MonthlySchedule demand,
        IReadOnlyList<MExternalAssignment>? externalAssignments = null,
        MonthlySchedulingSettings? monthlySettings = null)
    {
        var now = DateTimeOffset.UtcNow;
        var version = new ScheduleVersion
        {
            Workspace = workspace,
            Month = schedule.MonthStart,
            Name = $"求解 {now:yyyy-MM-dd HH:mm}－候選 {candidateIndex + 1}",
            SourceRunId = runId,
            CandidateIndex = candidateIndex,
            SourceStatus = sourceStatus,
            ConfigurationRevisionId = configurationRevisionId,
            MonthlySettingsJson = monthlySettings is null ? null : JsonSerializer.Serialize(monthlySettings, ServiceSupport.JsonOptions),
            CreatedByUserId = actorId,
            UpdatedByUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var demandByEmployee = demand.Employees.ToDictionary(x => x.EmployeeId, StringComparer.Ordinal);
        foreach (var employee in schedule.Employees)
        {
            var demandEmployee = demandByEmployee[employee.EmployeeId];
            var snapshot = new ScheduleEmployeeSnapshot
            {
                EmployeeCode = employee.EmployeeId,
                Name = employee.Name,
                Affiliation = employee.Affiliation,
                EmploymentStartDate = employee.EmploymentStartDate,
                Ability = employee.Ability,
                MonthlyShift = ShiftText(employee.MonthlyShift),
                OpeningRest = employee.OpeningUsage?.Rest,
                OpeningSpecialRest = employee.OpeningUsage?.SpecialRest,
                RequestedLeaveRestCount = demandEmployee.RequestedLeaveRestCount ?? 0,
                ClosingRest = employee.ClosingUsage?.Rest,
                ClosingSpecialRest = employee.ClosingUsage?.SpecialRest,
                NormalWorkCount = employee.NormalWorkCount,
                PerpetualScheduleId = employee.PerpetualScheduleId
            };
            snapshot.Assignments.AddRange(employee.Assignments.Select(pair => new ScheduleAssignment
            {
                Date = pair.Key,
                Kind = pair.Value.Kind?.ToString() ?? "Unresolved",
                RequestedRest = pair.Value.RequestedRest,
                Station = pair.Value.Station,
                Shift = ShiftText(pair.Value.Shift),
                EventStart = pair.Value.EventStart,
                EventEnd = pair.Value.EventEnd,
                EventDescription = pair.Value.EventDescription
            }));
            version.Employees.Add(snapshot);
        }
        if (externalAssignments is not null)
            version.ExternalAssignments.AddRange(externalAssignments.Select(x => new ExternalAssignment
            {
                Date = x.Date,
                Station = x.Station,
                Shift = ShiftText(x.Shift)!,
                Count = x.Count
            }));
        return version;
    }

    public static ScheduleVersion ToImportedVersion(
        MonthlySchedule schedule,
        WorkspaceCode workspace,
        Guid configurationRevisionId,
        Guid actorId)
    {
        var now = DateTimeOffset.UtcNow;
        var version = new ScheduleVersion
        {
            Workspace = workspace,
            Month = schedule.MonthStart,
            Name = $"上傳 {schedule.MonthStart:yyyy-MM} 班表",
            SourceStatus = ScheduleRunStatus.Imported,
            ConfigurationRevisionId = configurationRevisionId,
            CreatedByUserId = actorId,
            UpdatedByUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        foreach (var employee in schedule.Employees)
        {
            var snapshot = new ScheduleEmployeeSnapshot
            {
                EmployeeCode = employee.EmployeeId,
                Name = employee.Name,
                Affiliation = employee.Affiliation,
                EmploymentStartDate = employee.EmploymentStartDate,
                Ability = employee.Ability,
                MonthlyShift = ShiftText(employee.MonthlyShift),
                OpeningRest = employee.OpeningUsage?.Rest,
                OpeningSpecialRest = employee.OpeningUsage?.SpecialRest,
                RequestedLeaveRestCount = employee.Assignments.Values.Count(x => x.Kind == SolverAssignmentKind.LeaveRest),
                ClosingRest = employee.ClosingUsage?.Rest,
                ClosingSpecialRest = employee.ClosingUsage?.SpecialRest,
                NormalWorkCount = employee.NormalWorkCount,
                PerpetualScheduleId = employee.PerpetualScheduleId
            };
            snapshot.Assignments.AddRange(employee.Assignments.Select(pair => new ScheduleAssignment
            {
                Date = pair.Key,
                Kind = pair.Value.Kind?.ToString() ?? "Unresolved",
                RequestedRest = pair.Value.RequestedRest,
                Station = pair.Value.Station,
                Shift = ShiftText(pair.Value.Shift),
                EventStart = pair.Value.EventStart,
                EventEnd = pair.Value.EventEnd,
                EventDescription = pair.Value.EventDescription
            }));
            version.Employees.Add(snapshot);
        }
        return version;
    }

    public static DemandEmployee ToDemandEmployee(EmployeeMonthlySchedule employee)
    {
        var result = new DemandEmployee
        {
            EmployeeCode = employee.EmployeeId,
            Name = employee.Name,
            Affiliation = employee.Affiliation,
            EmploymentStartDate = employee.EmploymentStartDate,
            Ability = employee.Ability,
            MonthlyShift = ShiftText(employee.MonthlyShift),
            OpeningRest = employee.OpeningUsage?.Rest,
            OpeningSpecialRest = employee.OpeningUsage?.SpecialRest,
            RequestedLeaveRestCount = employee.RequestedLeaveRestCount ?? 0,
            PerpetualScheduleId = employee.PerpetualScheduleId
        };
        result.Assignments.AddRange(employee.Assignments.Select(pair => new DemandAssignment
        {
            Date = pair.Key,
            Kind = pair.Value.Kind?.ToString(),
            RequestedRest = pair.Value.RequestedRest,
            Station = pair.Value.Station,
            Shift = ShiftText(pair.Value.Shift),
            EventStart = pair.Value.EventStart,
            EventEnd = pair.Value.EventEnd,
            EventDescription = pair.Value.EventDescription
        }));
        return result;
    }

    private static ScheduleCell ToScheduleCell(DemandAssignment assignment) => new()
    {
        Kind = ParseKind(assignment.Kind),
        RequestedRest = assignment.RequestedRest,
        Station = assignment.Station,
        Shift = ParseShift(assignment.Shift),
        EventStart = assignment.EventStart,
        EventEnd = assignment.EventEnd,
        EventDescription = assignment.EventDescription
    };

    private static ScheduleCell ToScheduleCell(ScheduleAssignment assignment) => new()
    {
        Kind = ParseKind(assignment.Kind),
        RequestedRest = assignment.RequestedRest,
        Station = assignment.Station,
        Shift = ParseShift(assignment.Shift),
        EventStart = assignment.EventStart,
        EventEnd = assignment.EventEnd,
        EventDescription = assignment.EventDescription
    };

    public static SolverShift? ParseShift(string? value) => value switch
    {
        "Early" or "早" => SolverShift.Early,
        "Afternoon" or "午" or "小" => SolverShift.Afternoon,
        "Night" or "夜" => SolverShift.Night,
        null or "" => null,
        _ => throw new DomainValidationException($"不支援的班別：{value}。")
    };

    public static string? ShiftText(SolverShift? shift) => shift?.ToString();

    public static SolverAssignmentKind? ParseKind(string? value) => value switch
    {
        null or "" or "Unresolved" => null,
        "Work" => SolverAssignmentKind.Work,
        "Rest" => SolverAssignmentKind.Rest,
        "SpecialRest" => SolverAssignmentKind.SpecialRest,
        "LeaveRest" => SolverAssignmentKind.LeaveRest,
        "WorkEvent" => SolverAssignmentKind.WorkEvent,
        _ => throw new DomainValidationException($"不支援的日格狀態：{value}。")
    };
}
