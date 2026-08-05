using System.Globalization;

namespace NtmScheduler.Core.Domain;

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth Parse(string text)
    {
        if (!DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            throw new FormatException($"月份格式錯誤（應為 yyyy-MM）：{text}");
        if (d.Month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(text), "月份必須為 1–12");
        return new YearMonth(d.Year, d.Month);
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public YearMonth Next() => Month == 12 ? new YearMonth(Year + 1, 1) : new YearMonth(Year, Month + 1);
    public YearMonth Previous() => Month == 1 ? new YearMonth(Year - 1, 12) : new YearMonth(Year, Month - 1);

    public int CompareTo(YearMonth other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);
}
