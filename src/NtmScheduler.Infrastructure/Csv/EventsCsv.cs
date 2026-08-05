using System.Globalization;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Csv;

public sealed record EventsCsvRow(
    string EmployeeId,
    FixedEventType Type,
    DateOnly? Date,
    DateTime? Start,
    DateTime? End,
    string? Description);

public static class EventsCsv
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz"
    ];

    public static IReadOnlyList<EventsCsvRow> Read(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        Require(header, ["employee_id", "type", "date", "start", "end", "description"]);

        var list = new List<EventsCsvRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 6)
                throw new FormatException($"events.csv 第 {i + 2} 列欄位數不足");

            var empId = row[0].Trim();
            var typeText = row[1].Trim();
            FixedEventType type = typeText switch
            {
                "R*" or "R＊" => FixedEventType.RStar,
                "X" or "x" => FixedEventType.X,
                _ => throw new FormatException($"events.csv 第 {i + 2} 列未知 type：{typeText}")
            };

            if (type == FixedEventType.RStar)
            {
                if (!DateOnly.TryParseExact(row[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                    throw new FormatException($"events.csv 第 {i + 2} 列 R* date 無法解析：{row[2]}");
                list.Add(new EventsCsvRow(empId, type, date, null, null, null));
            }
            else
            {
                var start = ParseDateTime(row[3], i + 2, "start");
                var end = ParseDateTime(row[4], i + 2, "end");
                var desc = string.IsNullOrWhiteSpace(row[5]) ? null : row[5].Trim();
                list.Add(new EventsCsvRow(empId, type, null, start, end, desc));
            }
        }

        return list;
    }

    public static byte[] Write(IEnumerable<EventsCsvRow> events)
    {
        var rows = new List<IEnumerable<string?>>
        {
            new[] { "employee_id", "type", "date", "start", "end", "description" }
        };

        foreach (var e in events)
        {
            if (e.Type == FixedEventType.RStar)
            {
                rows.Add([
                    e.EmployeeId,
                    "R*",
                    e.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    "",
                    "",
                    ""
                ]);
            }
            else
            {
                rows.Add([
                    e.EmployeeId,
                    "X",
                    "",
                    FormatDateTime(e.Start),
                    FormatDateTime(e.End),
                    e.Description ?? ""
                ]);
            }
        }

        return CsvWriter.WriteToBytes(rows);
    }

    private static DateTime ParseDateTime(string text, int row, string field)
    {
        text = text.Trim();
        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            // Convert to Taipei local wall time then store as Unspecified.
            var taipei = TimeZoneInfo.ConvertTime(dto, Core.Time.TaipeiTime.Zone);
            return DateTime.SpecifyKind(taipei.DateTime, DateTimeKind.Unspecified);
        }

        throw new FormatException($"events.csv 第 {row} 列 {field} 無法解析：{text}");
    }

    private static string FormatDateTime(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    private static void Require(IReadOnlyList<string> header, string[] expected)
    {
        if (header.Count < expected.Length)
            throw new FormatException("events.csv 欄位不足");
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(header[i], expected[i], StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"events.csv 預期欄位 {expected[i]}，實際為 {header[i]}");
        }
    }
}
