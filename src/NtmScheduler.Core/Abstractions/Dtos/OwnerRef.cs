namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record OwnerRef(AssignmentOwnerType OwnerType, long OwnerId);
