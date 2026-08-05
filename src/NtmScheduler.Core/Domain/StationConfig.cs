namespace NtmScheduler.Core.Domain;

public static class StationConfig
{
    public static readonly IReadOnlyList<string> AllStations =
    [
        "LB01", "LB02", "LB03", "LB04", "LB05", "LB06",
        "LB07", "LB08", "LB09", "LB10", "LB11", "LB12"
    ];

    public static readonly IReadOnlyDictionary<string, string> StationGroup = new Dictionary<string, string>
    {
        ["LB01"] = "A", ["LB02"] = "A", ["LB03"] = "A",
        ["LB04"] = "B", ["LB05"] = "B", ["LB06"] = "B",
        ["LB07"] = "C", ["LB08"] = "C", ["LB09"] = "C",
        ["LB10"] = "D", ["LB11"] = "D", ["LB12"] = "D"
    };

    public static readonly IReadOnlySet<string> NightStations =
        new HashSet<string> { "LB01", "LB06", "LB08", "LB12" };

    public static readonly IReadOnlySet<string> ExternalStations =
        new HashSet<string> { "LB02", "LB04", "LB11" };

    public static string GroupOf(string station) =>
        StationGroup.TryGetValue(station, out var g)
            ? g
            : throw new ArgumentException($"未知車站：{station}", nameof(station));

    public static IEnumerable<string> StationsInGroup(string group) =>
        StationGroup.Where(kv => kv.Value == group).Select(kv => kv.Key);

    public static IEnumerable<ShiftType> ShiftsForStation(string station)
    {
        yield return ShiftType.Morning;
        yield return ShiftType.Afternoon;
        if (NightStations.Contains(station))
            yield return ShiftType.Night;
    }
}

public static class ShiftTimeConfig
{
    public static (TimeOnly Start, TimeOnly End, bool EndsNextDay) M(ShiftType shift) => shift switch
    {
        ShiftType.Morning => (new TimeOnly(6, 30), new TimeOnly(14, 30), false),
        ShiftType.Afternoon => (new TimeOnly(14, 20), new TimeOnly(22, 20), false),
        ShiftType.Night => (new TimeOnly(22, 0), new TimeOnly(7, 0), true),
        _ => throw new ArgumentOutOfRangeException(nameof(shift))
    };

    public static (TimeOnly Start, TimeOnly End, bool EndsNextDay) T(ShiftType shift) => shift switch
    {
        ShiftType.Morning => (new TimeOnly(7, 0), new TimeOnly(15, 0), false),
        ShiftType.Afternoon => (new TimeOnly(15, 0), new TimeOnly(23, 0), false),
        ShiftType.Night => (new TimeOnly(23, 0), new TimeOnly(7, 0), true),
        _ => throw new ArgumentOutOfRangeException(nameof(shift))
    };

    public static (DateTime Start, DateTime End) Interval(Unit unit, DateOnly date, ShiftType shift)
    {
        var (start, end, endsNextDay) = unit == Unit.M ? M(shift) : T(shift);
        var startDt = date.ToDateTime(start);
        var endDt = endsNextDay ? date.AddDays(1).ToDateTime(end) : date.ToDateTime(end);
        return (startDt, endDt);
    }
}
