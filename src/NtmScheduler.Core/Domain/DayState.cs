namespace NtmScheduler.Core.Domain;

public enum DayStateType
{
    Shift,
    Rest,
    RStar,
    HolidayRest,
    X,
    Unassigned
}

/// <summary>
/// Single-day assignment state. HolidayRest is R1 (national-holiday rest).
/// Rest = R; RStar = satisfied R*; neither HolidayRest nor Rest reset the same way for GEN-H-02.
/// </summary>
public readonly record struct DayState(
    DayStateType Type,
    ShiftType? Shift = null,
    string? Station = null)
{
    public static DayState Work(ShiftType shift, string? station = null) =>
        new(DayStateType.Shift, shift, station);

    public static DayState Rest => new(DayStateType.Rest);
    public static DayState RStar => new(DayStateType.RStar);
    public static DayState HolidayRest => new(DayStateType.HolidayRest);
    public static DayState X => new(DayStateType.X);
    public static DayState Unassigned => new(DayStateType.Unassigned);

    public bool IsWorkDay => Type is DayStateType.Shift or DayStateType.X;
    public bool IsGeneralRest => Type is DayStateType.Rest or DayStateType.RStar;
    public bool IsAnyRest => Type is DayStateType.Rest or DayStateType.RStar or DayStateType.HolidayRest;
    public bool IsNormalShift => Type == DayStateType.Shift && Shift.HasValue;

    public string ToDisplay() => Type switch
    {
        DayStateType.Shift when Shift is { } s && Station is { } st => $"{st}-{s.ToDisplay()}",
        DayStateType.Shift when Shift is { } s => s.ToDisplay(),
        DayStateType.Rest => "R",
        DayStateType.RStar => "R*",
        DayStateType.HolidayRest => "R1",
        DayStateType.X => "X",
        DayStateType.Unassigned => "UNASSIGNED",
        _ => throw new InvalidOperationException($"無法顯示狀態：{Type}")
    };

    public static DayState ParseDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("空的班表狀態");

        return text.Trim() switch
        {
            "R" => Rest,
            "R*" => RStar,
            "R1" => HolidayRest,
            "X" => X,
            "UNASSIGNED" => Unassigned,
            "早" => Work(ShiftType.Morning),
            "午" => Work(ShiftType.Afternoon),
            "夜" => Work(ShiftType.Night),
            var t when t.Contains('-', StringComparison.Ordinal) => ParseCrossStation(t),
            _ => throw new FormatException($"未知班表狀態：{text}")
        };
    }

    private static DayState ParseCrossStation(string text)
    {
        var parts = text.Split('-', 2);
        if (parts.Length != 2)
            throw new FormatException($"跨站格式錯誤：{text}");
        return Work(ShiftTypeExtensions.ParseDisplay(parts[1]), parts[0]);
    }
}
