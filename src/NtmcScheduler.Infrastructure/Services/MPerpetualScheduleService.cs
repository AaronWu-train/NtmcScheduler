using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class MPerpetualScheduleService(IDbContextFactory<NtmcDbContext> dbFactory) : IMPerpetualScheduleService
{
    public async Task<MPerpetualScheduleDto?> GetAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto> UploadAsync(string fileName, Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, WorkspaceCode.M);
        var schedule = await UploadFile.ParseAsync(csv, ScheduleCsv.ReadMPerpetualSchedule, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var before = entity is null ? null : new { entity.FileName, PatternCount = Read(entity).Patterns.Count };
        if (entity is null)
        {
            entity = new MPerpetualScheduleTemplate();
            db.MPerpetualScheduleTemplates.Add(entity);
        }
        entity.FileName = Path.GetFileName(fileName);
        entity.ScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions);
        Touch(entity, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "MPerpetualScheduleUploaded", WorkspaceCode.M, "MPerpetualScheduleTemplate", entity.Id, before,
            new { entity.FileName, PatternCount = schedule.Patterns.Count });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto> SavePatternAsync(
        string? originalId,
        string id,
        IReadOnlyList<string> days,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, WorkspaceCode.M);
        id = id.Trim();
        if (id.Length == 0) throw new DomainValidationException("萬年班表代號不可空白。");
        if (days.Count != 56) throw new DomainValidationException("萬年班表必須包含 56 天。");
        var parsed = days.Select((value, index) => ScheduleCsv.ParseMPerpetualCell(value.Trim(), $"第 {index + 1} 天")).ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new DomainValidationException("請先上傳或建立全域萬年班表。");
        if (entity.RevisionToken != revisionToken) throw new ConcurrencyConflictException("萬年班表已被其他人修改，請重新整理。");
        var schedule = Read(entity);
        var patterns = schedule.Patterns.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (originalId is null)
        {
            if (!patterns.TryAdd(id, parsed)) throw new DomainValidationException($"萬年班表代號 '{id}' 不可重複。");
        }
        else
        {
            if (!patterns.Remove(originalId)) throw new DomainValidationException("找不到要修改的萬年班表代號。");
            if (!patterns.TryAdd(id, parsed)) throw new DomainValidationException($"萬年班表代號 '{id}' 不可重複。");
        }
        entity.ScheduleJson = JsonSerializer.Serialize(new MPerpetualSchedule(patterns), ServiceSupport.JsonOptions);
        Touch(entity, actor.UserId);
        ServiceSupport.AddAudit(db, actor, originalId is null ? "MPerpetualPatternCreated" : "MPerpetualPatternUpdated",
            WorkspaceCode.M, "MPerpetualScheduleTemplate", entity.Id, new { Id = originalId }, new { Id = id });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto?> DeletePatternAsync(string id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, WorkspaceCode.M);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new DomainValidationException("找不到全域萬年班表。");
        if (entity.RevisionToken != revisionToken) throw new ConcurrencyConflictException("萬年班表已被其他人修改，請重新整理。");
        var patterns = Read(entity).Patterns.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (!patterns.Remove(id)) throw new DomainValidationException("找不到要刪除的萬年班表代號。");
        entity.ScheduleJson = JsonSerializer.Serialize(new MPerpetualSchedule(patterns), ServiceSupport.JsonOptions);
        Touch(entity, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "MPerpetualPatternDeleted", WorkspaceCode.M, "MPerpetualScheduleTemplate", entity.Id, new { Id = id }, null);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<PerpetualScheduleFileDto> ExportAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new DomainValidationException("找不到全域萬年班表。");
        return new(entity.FileName, ScheduleCsv.WriteMPerpetualSchedule(Read(entity)));
    }

    internal static MPerpetualSchedule Read(MPerpetualScheduleTemplate entity) =>
        JsonSerializer.Deserialize<MPerpetualSchedule>(entity.ScheduleJson, ServiceSupport.JsonOptions)
        ?? throw new DomainValidationException("全域萬年班表無法讀取。");

    private static MPerpetualScheduleDto ToDto(MPerpetualScheduleTemplate entity) => new(
        entity.FileName,
        entity.UpdatedAtUtc,
        entity.RevisionToken,
        Read(entity).Patterns.OrderBy(x => x.Key, StringComparer.Ordinal).Select(pattern =>
        {
            var cells = pattern.Value;
            return new MPerpetualPatternDto(
                pattern.Key,
                cells.Select(ScheduleCsv.MPerpetualCellText).ToArray(),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Early),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Afternoon),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Night));
        }).ToArray());

    private static void Touch(MPerpetualScheduleTemplate entity, Guid actorId)
    {
        entity.UpdatedByUserId = actorId;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.RevisionToken = Guid.NewGuid();
    }
}
