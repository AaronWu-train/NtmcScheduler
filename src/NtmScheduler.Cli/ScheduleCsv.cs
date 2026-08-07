using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using NtmScheduler.Solvers;

namespace NtmScheduler.Cli;

public sealed class ScheduleCsvException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Converts the stable monthly-schedule CSV boundary to and from solver contracts.</summary>
public static partial class ScheduleCsv
{
    private static readonly string[] Headers =
    [
        "ID", "姓名", "所屬", "到職日期", "能力", "T月班別",
        "上月底區間已計R", "上月底區間已計R1",
        .. Enumerable.Range(1, 31).Select(day => day.ToString(CultureInfo.InvariantCulture)),
        "本月底區間已計R", "本月底區間已計R1", "本月正常班次數"
    ];

    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    public static MonthlySchedule ReadMonthly(string path, DateOnly monthStart)
    {
        if (monthStart.Day != 1) throw new ScheduleCsvException(nameof(monthStart), "Month must start on day one.");
        var rows = ReadRows(path);
        if (rows.Count == 0) throw new ScheduleCsvException(path, "CSV is empty.");
        if (!rows[0].SequenceEqual(Headers)) throw new ScheduleCsvException(path, "Monthly schedule headers do not match the required format.");

        var employees = new List<EmployeeMonthlySchedule>();
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.Length != Headers.Length) throw new ScheduleCsvException($"{path}:{rowNumber + 1}", $"Expected {Headers.Length} fields but found {row.Length}.");
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            employees.Add(ParseEmployee(row, rowNumber + 1, monthStart));
        }
        return new(monthStart, employees);
    }

    public static void WriteMonthly(string path, MonthlySchedule schedule)
    {
        var lines = new List<string> { Join(Headers) };
        foreach (var employee in schedule.Employees)
        {
            var values = new List<string>
            {
                employee.EmployeeId,
                employee.Name,
                employee.Affiliation,
                employee.EmploymentStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                employee.Ability?.ToString(CultureInfo.InvariantCulture) ?? "",
                ShiftText(employee.MonthlyShift),
                employee.OpeningUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "",
                employee.OpeningUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? ""
            };
            values.AddRange(Enumerable.Range(1, 31).Select(day => CellText(schedule, employee, day)));
            values.Add(employee.ClosingUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.ClosingUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.NormalWorkCount?.ToString(CultureInfo.InvariantCulture) ?? "");
            lines.Add(Join(values));
        }
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(true));
    }

    public static IReadOnlyList<RestInterval> ReadRestIntervals(string path)
    {
        var rows = ReadRows(path);
        string[] expected = ["區間開始日期", "區間結束日期", "國定假日日期"];
        if (rows.Count == 0 || !rows[0].SequenceEqual(expected))
            throw new ScheduleCsvException(path, "Rest-interval headers do not match the required format.");

        var intervals = new List<RestInterval>();
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Length != 3) throw new ScheduleCsvException($"{path}:{rowNumber + 1}", "Expected three fields.");
            var start = Date(row[0], $"row {rowNumber + 1} start");
            var end = Date(row[1], $"row {rowNumber + 1} end");
            var holidays = string.IsNullOrWhiteSpace(row[2])
                ? []
                : row[2].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => Date(value, $"row {rowNumber + 1} holiday"))
                    .ToHashSet();
            intervals.Add(new(start, end, holidays));
        }
        return intervals;
    }

    private static EmployeeMonthlySchedule ParseEmployee(string[] row, int rowNumber, DateOnly monthStart)
    {
        var field = $"row {rowNumber}";
        var ability = NullableInt(row[4], $"{field} 能力");
        var monthlyShift = NullableShift(row[5], $"{field} T月班別");
        var employee = new EmployeeMonthlySchedule
        {
            EmployeeId = row[0].Trim(),
            Name = row[1].Trim(),
            Affiliation = row[2].Trim(),
            EmploymentStartDate = NullableDate(row[3], $"{field} 到職日期"),
            Ability = ability,
            MonthlyShift = monthlyShift,
            OpeningUsage = Usage(row[6], row[7], $"{field} opening"),
            Assignments = new Dictionary<DateOnly, ScheduleCell>(),
            ClosingUsage = Usage(row[39], row[40], $"{field} closing"),
            NormalWorkCount = NullableInt(row[41], $"{field} 本月正常班次數")
        };

        var assignments = (Dictionary<DateOnly, ScheduleCell>)employee.Assignments;
        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        for (var day = 1; day <= 31; day++)
        {
            var text = row[7 + day].Trim();
            if (day > days)
            {
                if (text.Length > 0) throw new ScheduleCsvException($"{field} day {day}", "A non-existent calendar day must be blank.");
                continue;
            }
            if (text.Length == 0) continue;
            var date = monthStart.AddDays(day - 1);
            assignments[date] = ParseCell(text, date, monthlyShift, $"{field} day {day}");
        }
        return employee;
    }

    private static ScheduleCell ParseCell(string text, DateOnly date, Shift? monthlyShift, string field) => text switch
    {
        "R" => new() { Kind = AssignmentKind.Rest },
        "R1" => new() { Kind = AssignmentKind.SpecialRest },
        "R*" => new() { RequestedRest = true },
        "R*[R]" => new() { Kind = AssignmentKind.Rest, RequestedRest = true },
        "R*[R1]" => new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
        _ when EventPattern().Match(text) is { Success: true } match => EventCell(match, date, field),
        _ when monthlyShift is not null && ShiftFromText(text) is { } shift => new() { Kind = AssignmentKind.Work, Shift = shift },
        _ when MWorkPattern().Match(text) is { Success: true } match => new()
        {
            Kind = AssignmentKind.Work,
            Station = match.Groups[1].Value,
            Shift = ShiftFromText(match.Groups[2].Value)
        },
        _ => throw new ScheduleCsvException(field, $"Unsupported schedule cell '{text}'.")
    };

    private static ScheduleCell EventCell(Match match, DateOnly date, string field)
    {
        if (!TimeOnly.TryParseExact(match.Groups[1].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(match.Groups[2].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            throw new ScheduleCsvException(field, "X time must use HH:mm-HH:mm.");
        var start = new DateTimeOffset(date.ToDateTime(startTime), TaipeiOffset);
        var endDate = endTime <= startTime ? date.AddDays(1) : date;
        var end = new DateTimeOffset(endDate.ToDateTime(endTime), TaipeiOffset);
        if (end - start > TimeSpan.FromHours(24)) throw new ScheduleCsvException(field, "X cannot exceed 24 hours.");
        return new() { Kind = AssignmentKind.WorkEvent, EventStart = start, EventEnd = end };
    }

    private static string CellText(MonthlySchedule schedule, EmployeeMonthlySchedule employee, int day)
    {
        if (day > DateTime.DaysInMonth(schedule.MonthStart.Year, schedule.MonthStart.Month)) return "";
        var date = schedule.MonthStart.AddDays(day - 1);
        if ((employee.EmploymentStartDate is { } start && date < start) || !employee.Assignments.TryGetValue(date, out var cell)) return "";
        return cell.Kind switch
        {
            null when cell.RequestedRest => "R*",
            AssignmentKind.Rest when cell.RequestedRest => "R*[R]",
            AssignmentKind.SpecialRest when cell.RequestedRest => "R*[R1]",
            AssignmentKind.Rest => "R",
            AssignmentKind.SpecialRest => "R1",
            AssignmentKind.WorkEvent => $"X[{cell.EventStart:HH\\:mm}-{cell.EventEnd:HH\\:mm}]",
            AssignmentKind.Work when cell.Station is not null => cell.Station + ShiftText(cell.Shift),
            AssignmentKind.Work => ShiftText(cell.Shift),
            _ => ""
        };
    }

    private static List<string[]> ReadRows(string path)
    {
        if (!File.Exists(path)) throw new ScheduleCsvException(path, "File does not exist.");
        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        try
        {
            while (!parser.EndOfData) rows.Add(parser.ReadFields() ?? []);
        }
        catch (MalformedLineException exception)
        {
            throw new ScheduleCsvException(path, $"Malformed CSV near line {exception.Message}.");
        }
        return rows;
    }

    private static RestUsage? Usage(string rest, string specialRest, string field)
    {
        var first = NullableInt(rest, $"{field} R");
        var second = NullableInt(specialRest, $"{field} R1");
        if (first is null && second is null) return null;
        if (first is null || second is null) throw new ScheduleCsvException(field, "R and R1 usage must both be filled or both be blank.");
        if (first < 0 || second < 0) throw new ScheduleCsvException(field, "Usage cannot be negative.");
        return new(first.Value, second.Value);
    }

    private static int? NullableInt(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new ScheduleCsvException(field, $"'{text}' is not an integer.");
        return value;
    }

    private static DateOnly Date(string text, string field)
    {
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new ScheduleCsvException(field, $"'{text}' must use yyyy-MM-dd.");
        return value;
    }

    private static DateOnly? NullableDate(string text, string field) =>
        string.IsNullOrWhiteSpace(text) ? null : Date(text, field);

    private static Shift? NullableShift(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ShiftFromText(text.Trim()) ?? throw new ScheduleCsvException(field, $"Unsupported shift '{text}'.");
    }

    private static Shift? ShiftFromText(string text) => text switch
    {
        "早" => Shift.Early,
        "午" => Shift.Afternoon,
        "夜" => Shift.Night,
        _ => null
    };

    private static string ShiftText(Shift? shift) => shift switch
    {
        Shift.Early => "早",
        Shift.Afternoon => "午",
        Shift.Night => "夜",
        _ => ""
    };

    private static string Join(IEnumerable<string> values) => string.Join(',', values.Select(Escape));
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";

    [GeneratedRegex(@"^X\[(\d{2}:\d{2})-(\d{2}:\d{2})\]$", RegexOptions.CultureInvariant)]
    private static partial Regex EventPattern();

    [GeneratedRegex(@"^(LB(?:0[1-9]|1[0-2]))(早|午|夜)$", RegexOptions.CultureInvariant)]
    private static partial Regex MWorkPattern();
}
