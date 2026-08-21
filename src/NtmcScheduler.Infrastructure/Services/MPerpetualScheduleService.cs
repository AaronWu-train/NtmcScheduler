using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class MPerpetualScheduleService(IDbContextFactory<NtmcDbContext> dbFactory) : IMPerpetualScheduleService
{
    public async Task<MPerpetualScheduleDto?> GetAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireStationWorkspace(workspace);
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Workspace == workspace, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto> UploadAsync(WorkspaceCode workspace, string fileName, Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireStationWorkspace(workspace);
        ServiceSupport.RequireEditor(actor, workspace);
        var schedule = await UploadFile.ParseAsync(csv, path => ScheduleCsv.ReadMPerpetualSchedule(path, workspace), cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Workspace == workspace, cancellationToken);
        var before = entity is null ? null : new { entity.FileName, PatternCount = Read(entity).Patterns.Count };
        if (entity is null)
        {
            entity = new MPerpetualScheduleTemplate { Id = workspace == WorkspaceCode.M ? 1 : 2, Workspace = workspace };
            db.MPerpetualScheduleTemplates.Add(entity);
        }
        entity.FileName = Path.GetFileName(fileName);
        entity.ScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions);
        Touch(entity, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "MPerpetualScheduleUploaded", workspace, "MPerpetualScheduleTemplate", entity.Id, before,
            new { entity.FileName, PatternCount = schedule.Patterns.Count });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto> SavePatternAsync(
        WorkspaceCode workspace,
        string? originalId,
        string id,
        IReadOnlyList<string> days,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        RequireStationWorkspace(workspace);
        ServiceSupport.RequireEditor(actor, workspace);
        id = id.Trim();
        if (id.Length == 0) throw new DomainValidationException("萬年班表代號不可空白。");
        if (days.Count != 56) throw new DomainValidationException("萬年班表必須包含 56 天。");
        var parsed = days.Select((value, index) => ScheduleCsv.ParseMPerpetualCell(value.Trim(), $"第 {index + 1} 天", workspace)).ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Workspace == workspace, cancellationToken)
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
            workspace, "MPerpetualScheduleTemplate", entity.Id, new { Id = originalId }, new { Id = id });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<MPerpetualScheduleDto?> DeletePatternAsync(WorkspaceCode workspace, string id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireStationWorkspace(workspace);
        ServiceSupport.RequireEditor(actor, workspace);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.SingleOrDefaultAsync(x => x.Workspace == workspace, cancellationToken)
            ?? throw new DomainValidationException("找不到全域萬年班表。");
        if (entity.RevisionToken != revisionToken) throw new ConcurrencyConflictException("萬年班表已被其他人修改，請重新整理。");
        var patterns = Read(entity).Patterns.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (!patterns.Remove(id)) throw new DomainValidationException("找不到要刪除的萬年班表代號。");
        entity.ScheduleJson = JsonSerializer.Serialize(new MPerpetualSchedule(patterns), ServiceSupport.JsonOptions);
        Touch(entity, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "MPerpetualPatternDeleted", workspace, "MPerpetualScheduleTemplate", entity.Id, new { Id = id }, null);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<PerpetualScheduleFileDto> ExportAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        RequireStationWorkspace(workspace);
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MPerpetualScheduleTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Workspace == workspace, cancellationToken)
            ?? throw new DomainValidationException("找不到全域萬年班表。");
        return new(entity.FileName, ScheduleCsv.WriteMPerpetualSchedule(Read(entity), workspace));
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
                cells.Select(cell => ScheduleCsv.MPerpetualCellText(cell, entity.Workspace)).ToArray(),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Early),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Afternoon),
                cells.Count(x => x?.Kind == AssignmentKind.Work && x.Shift == Shift.Night));
        }).ToArray());

    private static void RequireStationWorkspace(WorkspaceCode workspace)
    {
        if (!workspace.IsStation()) throw new DomainValidationException("只有站務工作區使用萬年班表。");
    }

    private static void Touch(MPerpetualScheduleTemplate entity, Guid actorId)
    {
        entity.UpdatedByUserId = actorId;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entity.RevisionToken = Guid.NewGuid();
    }
}
