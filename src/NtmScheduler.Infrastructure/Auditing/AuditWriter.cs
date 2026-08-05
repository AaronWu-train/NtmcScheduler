using System.Text.Json;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Auditing;

public sealed class AuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NtmDbContext _db;

    public AuditWriter(NtmDbContext db) => _db = db;

    public void Add(
        string op,
        string action,
        string targetType,
        string targetId,
        object? before = null,
        object? after = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            At = TaipeiTime.Now,
            Operator = op,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions)
        });
    }

}
