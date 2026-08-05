namespace NtmScheduler.Core.Time;

public static class TaipeiTime
{
    public static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateTime SpecifyLocal(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Taipei Custom",
            TimeSpan.FromHours(8),
            "Taipei",
            "Taipei");
    }
}
