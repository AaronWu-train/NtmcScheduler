using System.Text.Json;
using System.Text;
using NtmScheduler.Contracts;
using NtmScheduler.Infrastructure.Data;

namespace NtmScheduler.Infrastructure.Services;

internal static class ServiceSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void RequireAdministrator(ActorContext actor)
    {
        if (actor.UserId == Guid.Empty) throw new ForbiddenOperationException("請先登入。");
        if (actor.MustChangePassword) throw new ForbiddenOperationException("首次登入必須先修改密碼。");
        if (!actor.IsAdministrator) throw new ForbiddenOperationException("此操作只允許系統管理者執行。");
    }

    public static void RequireViewer(ActorContext actor)
    {
        if (actor.UserId == Guid.Empty) throw new ForbiddenOperationException("請先登入。");
        if (actor.MustChangePassword) throw new ForbiddenOperationException("首次登入必須先修改密碼。");
    }

    public static void RequireEditor(ActorContext actor, WorkspaceCode workspace)
    {
        if (actor.UserId == Guid.Empty) throw new ForbiddenOperationException("請先登入。");
        if (actor.MustChangePassword) throw new ForbiddenOperationException("首次登入必須先修改密碼。");
        if (!actor.CanEdit(workspace)) throw new ForbiddenOperationException($"沒有 {workspace} 工作區的編輯權限。");
    }

    public static void AddAudit(
        NtmDbContext db,
        ActorContext actor,
        string action,
        WorkspaceCode? workspace,
        string resourceType,
        object resourceId,
        object? before,
        object? after,
        bool succeeded = true)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            AtUtc = now,
            AtUtcTicks = now.UtcTicks,
            ActorUserId = actor.UserId,
            ActorName = actor.UserName,
            Action = action,
            Workspace = workspace,
            ResourceType = resourceType,
            ResourceId = resourceId.ToString() ?? "",
            Succeeded = succeeded,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            IpAddress = actor.IpAddress,
            UserAgent = actor.UserAgent,
            CorrelationId = actor.CorrelationId
        });
    }

    public static EmployeeDto ToDto(Employee employee) => new(
        employee.Id,
        employee.Workspace,
        employee.EmployeeCode,
        employee.Name,
        employee.Affiliation,
        employee.EmploymentStartDate,
        employee.Ability,
        employee.RevisionToken);

    public static DemandDraftDto ToDto(DemandDraft demand) => new(
        demand.Id,
        demand.Workspace,
        demand.Month,
        demand.PreviousSource,
        demand.ConfigurationRevisionId,
        demand.RevisionToken,
        demand.UpdatedAtUtc,
        demand.Employees.OrderBy(x => x.EmployeeCode).Select(x => new DemandEmployeeDto(
            x.Id, x.EmployeeCode, x.Name, x.Affiliation, x.EmploymentStartDate, x.Ability,
            x.MonthlyShift, x.OpeningRest, x.OpeningSpecialRest, x.RequestedLeaveRestCount,
            x.PerpetualScheduleId,
            x.Assignments.OrderBy(a => a.Date).Select(a => new DemandAssignmentDto(
                a.Id, a.DemandEmployeeId, a.Date, a.Kind, a.RequestedRest, a.Station, a.Shift,
                a.EventStart, a.EventEnd, a.EventDescription)).ToArray())).ToArray());

    public static ConfigurationRevisionDto ToDto(ConfigurationRevision revision, Guid currentRevisionToken = default) => new(
        revision.Id,
        revision.Version,
        revision.CreatedAtUtc,
        currentRevisionToken,
        revision.RestIntervals.OrderBy(x => x.Start).Select(x => new RestIntervalDto(
            x.Start, x.End, x.NationalHolidays.OrderBy(h => h.Date).Select(h => h.Date).ToArray())).ToArray(),
        revision.NonStandardShifts.OrderBy(x => x.Code).Select(x => new NonStandardShiftDto(
            x.Name, x.Code, x.StartTime, x.EndTime)).ToArray());
}

internal static class UploadFile
{
    public const long MaximumBytes = 5 * 1024 * 1024;

    public static async Task<T> ParseAsync<T>(Stream source, Func<string, T> parser, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}.csv");
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes) throw new DomainValidationException("CSV 檔案不可超過 5 MB。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            try
            {
                _ = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                throw new DomainValidationException("CSV 必須使用 UTF-8 編碼，可含 BOM。");
            }
            return parser(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
