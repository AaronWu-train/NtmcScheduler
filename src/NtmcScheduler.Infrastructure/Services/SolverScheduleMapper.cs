using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;
using SolverAssignmentKind = NtmcScheduler.Solvers.AssignmentKind;
using SolverShift = NtmcScheduler.Solvers.Shift;

namespace NtmcScheduler.Infrastructure.Services;

internal static class SolverScheduleMapper
{
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
            Assignments = employee.Assignments.ToDictionary(x => x.Date, assignment =>
                demand.Workspace == WorkspaceCode.M && IsOutsideStationGroup(employee.Affiliation, assignment)
                    ? ToWorkEvent(assignment)
                    : ToScheduleCell(assignment)),
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

    public static ScheduleVersion ToVersion(
        MonthlySchedule schedule,
        WorkspaceCode workspace,
        Guid runId,
        int candidateIndex,
        ScheduleRunStatus sourceStatus,
        Guid configurationRevisionId,
        Guid actorId,
        MonthlySchedule demand,
        IReadOnlyList<MExternalAssignment>? externalAssignments = null)
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

    private static bool IsOutsideStationGroup(string affiliation, DemandAssignment assignment) =>
        assignment.Kind == "Work" && affiliation is { Length: 4 } && assignment.Station is { Length: 4 } station &&
        int.TryParse(affiliation[2..], out var home) && int.TryParse(station[2..], out var assigned) &&
        (home - 1) / 3 != (assigned - 1) / 3;

    private static ScheduleCell ToWorkEvent(DemandAssignment assignment)
    {
        var shift = ParseShift(assignment.Shift)!.Value;
        var (start, end, nextDay, text) = shift switch
        {
            SolverShift.Early => (new TimeOnly(6, 30), new TimeOnly(14, 30), false, "早"),
            SolverShift.Afternoon => (new TimeOnly(14, 20), new TimeOnly(22, 20), false, "小"),
            SolverShift.Night => (new TimeOnly(22, 0), new TimeOnly(7, 0), true, "夜"),
            _ => throw new ArgumentOutOfRangeException(nameof(assignment))
        };
        var date = assignment.Date;
        return new()
        {
            Kind = SolverAssignmentKind.WorkEvent,
            EventStart = new DateTimeOffset(date.ToDateTime(start), TimeSpan.FromHours(8)),
            EventEnd = new DateTimeOffset(date.AddDays(nextDay ? 1 : 0).ToDateTime(end), TimeSpan.FromHours(8)),
            EventDescription = $"{assignment.Station}{text}"
        };
    }

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
