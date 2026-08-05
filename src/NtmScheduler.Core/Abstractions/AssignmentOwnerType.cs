namespace NtmScheduler.Core.Abstractions;

public enum AssignmentOwnerType
{
    Candidate,
    /// <summary>Editable current month schedule (one per unit/month).</summary>
    Schedule,
    /// <summary>Immutable snapshot (history import or manual snapshot).</summary>
    Snapshot
}
