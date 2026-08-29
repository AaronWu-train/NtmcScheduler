using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class CommonConfigurationService(IDbContextFactory<NtmcDbContext> dbFactory) : ICommonConfigurationService
{
    public async Task<ConfigurationRevisionDto?> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var current = await db.CurrentConfigurations.AsNoTracking().AsSplitQuery()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.StandardShiftTimes)
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return current is null ? null : ServiceSupport.ToDto(current.ConfigurationRevision, current.RevisionToken);
    }

    public async Task<ConfigurationRevisionDto?> GetRevisionAsync(Guid id, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var revision = await db.ConfigurationRevisions.AsNoTracking().AsSplitQuery()
            .Include(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.NonStandardShifts)
            .Include(x => x.StandardShiftTimes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return revision is null ? null : ServiceSupport.ToDto(revision);
    }

    public async Task<IReadOnlyList<RestIntervalDto>> ParseRestIntervalsCsvAsync(Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireEditor(actor);
        var intervals = await UploadFile.ParseAsync(csv, ScheduleCsv.ReadRestIntervals, cancellationToken);
        return intervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.Order().ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<NonStandardShiftDto>> ParseNonStandardShiftsCsvAsync(Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireEditor(actor);
        var shifts = await UploadFile.ParseAsync(csv, ScheduleCsv.ReadNonStandardShifts, cancellationToken);
        return shifts.Shifts.Select(x => new NonStandardShiftDto(x.Name, x.Code, x.StartTime, x.EndTime)).ToArray();
    }

    public async Task<ConfigurationRevisionDto> CreateRevisionAsync(
        IReadOnlyList<RestIntervalDto> intervals,
        IReadOnlyList<NonStandardShiftDto> shifts,
        Guid? currentRevisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        RequireEditor(actor);
        Validate(intervals, shifts);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.CurrentConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (current is null && currentRevisionToken is not null || current is not null && current.RevisionToken != currentRevisionToken)
            throw new ConcurrencyConflictException("共同設定已被其他人修改，請重新整理。");
        var version = (await db.ConfigurationRevisions.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var revision = new ConfigurationRevision { Version = version, CreatedByUserId = actor.UserId };
        revision.RestIntervals.AddRange(intervals.Select(interval => new RestIntervalEntity
        {
            Start = interval.Start,
            End = interval.End,
            NationalHolidays = interval.NationalHolidays.Select(date => new NationalHoliday { Date = date }).ToList()
        }));
        revision.NonStandardShifts.AddRange(shifts.Select(shift => new NonStandardShiftEntity
        {
            Name = string.IsNullOrWhiteSpace(shift.Name) ? null : shift.Name.Trim(),
            Code = shift.Code.Trim(),
            StartTime = shift.StartTime,
            EndTime = shift.EndTime
        }));
        var currentRevision = current is null ? null : await LoadRevisionAsync(db, current.ConfigurationRevisionId, cancellationToken);
        AddStandardShiftTimes(revision, currentRevision);
        db.ConfigurationRevisions.Add(revision);
        if (current is null) db.CurrentConfigurations.Add(new() { ConfigurationRevision = revision });
        else
        {
            current.ConfigurationRevision = revision;
            current.RevisionToken = Guid.NewGuid();
        }
        ServiceSupport.AddAudit(db, actor, "ConfigurationRevisionCreated", null, "ConfigurationRevision", revision.Id, null,
            new { revision.Version, RestIntervals = intervals.Count, NonStandardShifts = shifts.Count });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var savedCurrent = await db.CurrentConfigurations.AsNoTracking().SingleAsync(x => x.Id == 1, cancellationToken);
        return ServiceSupport.ToDto(revision, savedCurrent.RevisionToken);
    }

    private static void Validate(IReadOnlyList<RestIntervalDto> intervals, IReadOnlyList<NonStandardShiftDto> shifts)
    {
        if (intervals.Count == 0) throw new DomainValidationException("至少需要一個八週區間。");
        var ordered = intervals.OrderBy(x => x.Start).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var interval = ordered[index];
            if (interval.End.DayNumber - interval.Start.DayNumber != 55)
                throw new DomainValidationException($"{interval.Start:yyyy-MM-dd} 起的區間必須剛好 56 日。");
            if (index > 0 && ordered[index - 1].End.AddDays(1) != interval.Start)
                throw new DomainValidationException("八週區間必須連續、不可重疊或留有缺口。");
            if (interval.NationalHolidays.Distinct().Count() != interval.NationalHolidays.Count ||
                interval.NationalHolidays.Any(date => date < interval.Start || date > interval.End || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
                throw new DomainValidationException("國定假日必須唯一、位於所屬區間內且為週一至週五。");
        }

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shift in shifts)
        {
            var names = ScheduleCsv.NonStandardShiftNames(shift.Name);
            if (string.IsNullOrWhiteSpace(shift.Code) || !tokens.Add(shift.Code.Trim()) || ScheduleCsv.IsReservedShiftName(shift.Code.Trim()) ||
                names.Any(name => !tokens.Add(name)) || names.Any(ScheduleCsv.IsReservedShiftName))
                throw new DomainValidationException("非常態班型代碼與各名稱必須唯一且不可使用早、午、小、夜；多個名稱以分號分隔。");
        }
    }

    public async Task<ConfigurationRevisionDto> UpdateWorkspaceShiftTimesAsync(
        WorkspaceCode workspace,
        WorkspaceShiftTimesDto times,
        Guid currentRevisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        if (!actor.CanEdit(workspace))
            throw new ForbiddenOperationException($"只有 {workspace} 工作區編輯者可修改班別時間。");
        ValidateShiftTimes(times);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.CurrentConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new DomainValidationException("尚未建立共同設定，請先儲存八週區間。");
        if (current.RevisionToken != currentRevisionToken)
            throw new ConcurrencyConflictException("共同設定已被其他人修改，請重新整理。");
        var previousRevision = await LoadRevisionAsync(db, current.ConfigurationRevisionId, cancellationToken);
        var version = (await db.ConfigurationRevisions.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var revision = new ConfigurationRevision { Version = version, CreatedByUserId = actor.UserId };
        revision.RestIntervals.AddRange(previousRevision.RestIntervals.Select(x => new RestIntervalEntity
        {
            Start = x.Start,
            End = x.End,
            NationalHolidays = x.NationalHolidays.Select(h => new NationalHoliday { Date = h.Date }).ToList()
        }));
        revision.NonStandardShifts.AddRange(previousRevision.NonStandardShifts.Select(x => new NonStandardShiftEntity
        {
            Name = x.Name,
            Code = x.Code,
            StartTime = x.StartTime,
            EndTime = x.EndTime
        }));
        // Copy existing StandardShiftTimes from previous revision, overwrite the workspace being updated.
        foreach (var existing in previousRevision.StandardShiftTimes)
        {
            if (existing.Workspace == workspace.ToString()) continue;
            revision.StandardShiftTimes.Add(new StandardShiftTimeEntity
            {
                Workspace = existing.Workspace,
                Shift = existing.Shift,
                StartTime = existing.StartTime,
                EndTime = existing.EndTime
            });
        }
        AddWorkspaceShiftTimes(revision, workspace.ToString(), times);
        db.ConfigurationRevisions.Add(revision);
        current.ConfigurationRevision = revision;
        current.RevisionToken = Guid.NewGuid();
        ServiceSupport.AddAudit(db, actor, "ConfigurationRevisionCreated", workspace, "ConfigurationRevision", revision.Id, null,
            new { revision.Version, Workspace = workspace.ToString(), ShiftTimes = times });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var savedCurrent = await db.CurrentConfigurations.AsNoTracking().SingleAsync(x => x.Id == 1, cancellationToken);
        return ServiceSupport.ToDto(revision, savedCurrent.RevisionToken);
    }

    private static async Task<ConfigurationRevision> LoadRevisionAsync(NtmcDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.ConfigurationRevisions.AsNoTracking().AsSplitQuery()
            .Include(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.NonStandardShifts)
            .Include(x => x.StandardShiftTimes)
            .SingleAsync(x => x.Id == id, cancellationToken);

    private static void AddStandardShiftTimes(ConfigurationRevision revision, ConfigurationRevision? previous)
    {
        // Copy from previous if available; otherwise seed with hard-coded defaults.
        foreach (var ws in new[] { "M", "T", "YM", "YT" })
        {
            var defaults = ws is "T" or "YT" ? WorkspaceShiftTimes.DefaultT : WorkspaceShiftTimes.DefaultM;
            foreach (var (shift, fallback) in new[] { ("Early", defaults.Early), ("Afternoon", defaults.Afternoon), ("Night", defaults.Night) })
            {
                var existing = previous?.StandardShiftTimes.FirstOrDefault(x => x.Workspace == ws && x.Shift == shift);
                revision.StandardShiftTimes.Add(new StandardShiftTimeEntity
                {
                    Workspace = ws,
                    Shift = shift,
                    StartTime = existing?.StartTime ?? fallback.Start,
                    EndTime = existing?.EndTime ?? fallback.End
                });
            }
        }
    }

    private static void AddWorkspaceShiftTimes(ConfigurationRevision revision, string workspace, WorkspaceShiftTimesDto times)
    {
        foreach (var (shift, pair) in new[] { ("Early", times.Early), ("Afternoon", times.Afternoon), ("Night", times.Night) })
        {
            revision.StandardShiftTimes.Add(new StandardShiftTimeEntity
            {
                Workspace = workspace,
                Shift = shift,
                StartTime = pair.Start,
                EndTime = pair.End
            });
        }
    }

    private static void ValidateShiftTimes(WorkspaceShiftTimesDto times)
    {
        foreach (var (label, pair) in new[] { ("早班", times.Early), ("午班", times.Afternoon), ("夜班", times.Night) })
        {
            if (pair.Start == pair.End)
                throw new DomainValidationException($"{label} 起訖時間不可相同。");
        }
    }

    private static void RequireEditor(ActorContext actor)
    {
        ServiceSupport.RequireViewer(actor);
        if (!actor.IsAdministrator && actor.EditableWorkspaces.Count == 0)
            throw new ForbiddenOperationException("只有工作區編輯者可修改共同設定。");
    }
}
