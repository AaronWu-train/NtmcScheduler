namespace NtmScheduler.Core.Domain;

public enum ShiftType
{
    Morning,
    Afternoon,
    Night
}

public static class ShiftTypeExtensions
{
    public static string ToDisplay(this ShiftType shift) => shift switch
    {
        ShiftType.Morning => "早",
        ShiftType.Afternoon => "午",
        ShiftType.Night => "夜",
        _ => throw new ArgumentOutOfRangeException(nameof(shift), shift, null)
    };

    public static ShiftType ParseDisplay(string text) => text switch
    {
        "早" => ShiftType.Morning,
        "午" => ShiftType.Afternoon,
        "夜" => ShiftType.Night,
        _ => throw new FormatException($"未知班別：{text}")
    };

    public static ShiftType NextInRotation(this ShiftType shift) => shift switch
    {
        ShiftType.Morning => ShiftType.Afternoon,
        ShiftType.Afternoon => ShiftType.Night,
        ShiftType.Night => ShiftType.Morning,
        _ => throw new ArgumentOutOfRangeException(nameof(shift), shift, null)
    };
}
