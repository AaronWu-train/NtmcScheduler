using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class CommonConfigurationService(NtmcDbContext db) : ICommonConfigurationService
{
    public async Task<ConfigurationRevisionDto?> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var current = await db.CurrentConfigurations.AsNoTracking()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return current is null ? null : ServiceSupport.ToDto(current.ConfigurationRevision, current.RevisionToken);
    }

    public async Task<ConfigurationRevisionDto?> GetRevisionAsync(Guid id, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var revision = await db.ConfigurationRevisions.AsNoTracking()
            .Include(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.NonStandardShifts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return revision is null ? null : ServiceSupport.ToDto(revision);
    }

    public async Task<ConfigurationRevisionDto> CreateRevisionAsync(
        IReadOnlyList<RestIntervalDto> intervals,
        IReadOnlyList<NonStandardShiftDto> shifts,
        Guid? currentRevisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        if (!actor.IsAdministrator && actor.EditableWorkspaces.Count == 0)
            throw new ForbiddenOperationException("只有工作區編輯者可修改共同設定。");
        Validate(intervals, shifts);
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
            if (string.IsNullOrWhiteSpace(shift.Code) || !tokens.Add(shift.Code.Trim()) ||
                !string.IsNullOrWhiteSpace(shift.Name) && !tokens.Add(shift.Name.Trim()))
                throw new DomainValidationException("非常態班型名稱與代碼必填／唯一，且不可互相重複。");
        }
    }
}
