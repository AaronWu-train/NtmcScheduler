using NtmcScheduler.Contracts;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

internal static class DemandCellValidator
{
    public static void Validate(
        WorkspaceCode workspace,
        DateOnly date,
        string? kind,
        bool requestedRest,
        string? station,
        string? shift,
        DateTimeOffset? eventStart,
        DateTimeOffset? eventEnd,
        string? description)
    {
        if (kind is not (null or "" or "Unresolved" or "Work" or "Rest" or "SpecialRest" or "LeaveRest" or "WorkEvent"))
            throw new DomainValidationException("不支援的需求日格狀態。");
        if (requestedRest && kind is not (null or "" or "Unresolved" or "Rest" or "SpecialRest" or "LeaveRest"))
            throw new DomainValidationException("R* 標記只能套用在未決定或休假日格。");
        if (kind == "Work")
        {
            if (SolverScheduleMapper.ParseShift(shift) is null) throw new DomainValidationException("正常班必須指定班別。");
            if (workspace == WorkspaceCode.M && !IsMStation(station)) throw new DomainValidationException("M 正常班車站必須為 LB01–LB12。");
            if (workspace == WorkspaceCode.T && !string.IsNullOrWhiteSpace(station)) throw new DomainValidationException("T 正常班不可指定車站。");
        }
        if (kind == "WorkEvent" && (eventStart is null || eventEnd is null || eventEnd <= eventStart || eventEnd - eventStart > TimeSpan.FromHours(24) || eventStart.Value.Offset != TimeSpan.FromHours(8) || eventEnd.Value.Offset != TimeSpan.FromHours(8)))
            throw new DomainValidationException("X 必須使用台北時間，結束晚於開始且長度不超過 24 小時。");
        if (kind == "WorkEvent" && DateOnly.FromDateTime(eventStart!.Value.DateTime) != date)
            throw new DomainValidationException("X 必須歸在台北時間的開始日期。");
        if (description?.Length > 500) throw new DomainValidationException("X 說明不可超過 500 字元。");
    }

    private static bool IsMStation(string? station) => station is not null && station.Length == 4 &&
        station.StartsWith("LB", StringComparison.Ordinal) && int.TryParse(station[2..], out var number) && number is >= 1 and <= 12;
}
