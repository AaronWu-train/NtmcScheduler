namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string Operator { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
