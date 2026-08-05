namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record AuditLogDto(
    long Id,
    DateTime At,
    string Operator,
    string Action,
    string TargetType,
    string TargetId,
    string? BeforeJson,
    string? AfterJson);

public sealed record AuditQuery(
    string? Operator = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Take = 100);
