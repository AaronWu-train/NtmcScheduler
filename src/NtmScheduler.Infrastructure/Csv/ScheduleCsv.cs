using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using NtmScheduler.Solvers;

namespace NtmScheduler.Infrastructure.Csv;

public sealed class ScheduleCsvException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Converts the stable monthly-schedule CSV boundary to and from solver contracts.</summary>
public static partial class ScheduleCsv
{
    private static readonly string[] LegacyHeaders =
    [
        "ID", "姓名", "所屬", "到職日期", "能力", "T月班別",
        "月初區間累計R", "月初區間累計R1",
        .. Enumerable.Range(1, 31).Select(day => day.ToString(CultureInfo.InvariantCulture)),
        "當月R", "當月R1", "當月指定R休", "月底區間累計R", "月底區間累計R1", "本月班數"
    ];
    private static readonly string[] Headers = [.. LegacyHeaders, "萬年班表"];

    public static string MonthlyHeader => Join(Headers);
    public static string MPerpetualHeader => Join(["萬年班表", .. Enumerable.Range(1, 56).Select(day => day.ToString(CultureInfo.InvariantCulture))]);

    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    public static MonthlySchedule ReadMonthly(string path, DateOnly monthStart, NonStandardShiftTable? nonStandardShifts = null, bool historical = false)
    {
        if (monthStart.Day != 1) throw new ScheduleCsvException(nameof(monthStart), "Month must start on day one.");
        var rows = ReadRows(path);
        if (rows.Count == 0) throw new ScheduleCsvException(path, "CSV is empty.");
        var hasPerpetualSchedule = rows[0].SequenceEqual(Headers);
        if (!hasPerpetualSchedule && !rows[0].SequenceEqual(LegacyHeaders))
            throw new ScheduleCsvException(path, "Monthly schedule headers do not match the required format.");
        var fieldCount = hasPerpetualSchedule ? Headers.Length : LegacyHeaders.Length;
        var nonStandardShiftLookup = NonStandardShiftLookup(nonStandardShifts);

        var employees = new List<EmployeeMonthlySchedule>();
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.Length != fieldCount) throw new ScheduleCsvException($"{path}:{rowNumber + 1}", $"Expected {fieldCount} fields but found {row.Length}.");
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            employees.Add(ParseEmployee(row, rowNumber + 1, monthStart, nonStandardShiftLookup, historical, hasPerpetualSchedule));
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
            var monthlyUsage = employee.ClosingUsage is null ? null : CountRestUsage(employee.Assignments.Values);
            values.Add(monthlyUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(monthlyUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add((employee.ClosingUsage is null
                ? employee.RequestedLeaveRestCount
                : employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest))?.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.ClosingUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.ClosingUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.NormalWorkCount?.ToString(CultureInfo.InvariantCulture) ?? "");
            values.Add(employee.PerpetualScheduleId ?? "");
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

    public static NonStandardShiftTable ReadNonStandardShifts(string path)
    {
        var rows = ReadRows(path);
        string[] expected = ["班型", "時間", "代碼"];
        if (rows.Count == 0 || !rows[0].SequenceEqual(expected))
            throw new ScheduleCsvException(path, "非常態班型 CSV 表頭不符合規定。");

        var shifts = new List<NonStandardShift>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var field = $"{path}:{rowNumber + 1}";
            if (row.Length != 3) throw new ScheduleCsvException(field, "非常態班型資料應為三欄。");
            var name = string.IsNullOrWhiteSpace(row[0]) ? null : row[0].Trim();
            var times = row[1].Trim().Split('~');
            var code = row[2].Trim();
            if (code.Length == 0) throw new ScheduleCsvException(field, "非常態班型代碼不可空白。");
            if (times.Length != 2 ||
                !TimeOnly.TryParseExact(times[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
                !TimeOnly.TryParseExact(times[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
                throw new ScheduleCsvException(field, "非常態班型時間必須使用 HH:mm~HH:mm。");
            if (!tokens.Add(code) || name is not null && !tokens.Add(name))
                throw new ScheduleCsvException(field, "非常態班型名稱與代碼不可重複。");
            shifts.Add(new(name, startTime, endTime, code));
        }
        return new(shifts);
    }

    public static MPerpetualSchedule ReadMPerpetualSchedule(string path)
    {
        var rows = ReadRows(path);
        string[] expected = ["萬年班表", .. Enumerable.Range(1, 56).Select(day => day.ToString(CultureInfo.InvariantCulture))];
        if (rows.Count == 0 || !rows[0].SequenceEqual(expected))
            throw new ScheduleCsvException(path, "M perpetual-schedule headers do not match the required format.");

        var patterns = new Dictionary<string, IReadOnlyList<ScheduleCell?>>(StringComparer.Ordinal);
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var field = $"{path}:{rowNumber + 1}";
            if (row.Length != expected.Length) throw new ScheduleCsvException(field, $"Expected {expected.Length} fields but found {row.Length}.");
            var id = row[0].Trim();
            if (id.Length == 0) throw new ScheduleCsvException(field, "萬年班表代號不可空白。");
            if (!patterns.TryAdd(id, row.Skip(1).Select((text, index) => ParseMPerpetualCell(text.Trim(), $"{field} day {index + 1}")).ToArray()))
                throw new ScheduleCsvException(field, $"萬年班表代號 '{id}' 不可重複。");
        }
        return new(patterns);
    }

    private static EmployeeMonthlySchedule ParseEmployee(
        string[] row,
        int rowNumber,
        DateOnly monthStart,
        IReadOnlyDictionary<string, NonStandardShift> nonStandardShifts,
        bool historical,
        bool hasPerpetualSchedule)
    {
        var field = $"row {rowNumber}";
        var ability = NullableInt(row[4], $"{field} 能力");
        var monthlyShift = NullableShift(row[5], $"{field} T月班別");
        var monthlyUsage = Usage(row[39], row[40], $"{field} monthly");
        var monthlyLeaveRest = NullableInt(row[41], $"{field} 當月指定R休");
        var closingUsage = Usage(row[42], row[43], $"{field} closing");
        var employee = new EmployeeMonthlySchedule
        {
            EmployeeId = row[0].Trim(),
            Name = row[1].Trim(),
            Affiliation = row[2].Trim(),
            EmploymentStartDate = NullableDate(row[3], $"{field} 到職日期"),
            Ability = ability,
            MonthlyShift = monthlyShift,
            PerpetualScheduleId = hasPerpetualSchedule && !string.IsNullOrWhiteSpace(row[45]) ? row[45].Trim() : null,
            RequestedLeaveRestCount = closingUsage is null ? monthlyLeaveRest : null,
            OpeningUsage = Usage(row[6], row[7], $"{field} opening"),
            Assignments = new Dictionary<DateOnly, ScheduleCell>(),
            ClosingUsage = closingUsage,
            NormalWorkCount = NullableInt(row[44], $"{field} 本月班數")
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
            assignments[date] = ParseCell(text, date, monthlyShift, nonStandardShifts, historical, $"{field} day {day}");
        }
        var expectedMonthlyUsage = CountRestUsage(assignments.Values);
        if (closingUsage is null && monthlyUsage is not null)
            throw new ScheduleCsvException(field, "Monthly R/R1 must be blank when closing interval totals are blank.");
        if (closingUsage is not null && monthlyUsage is null)
            throw new ScheduleCsvException(field, "Monthly R/R1 is required when closing interval totals are filled.");
        if (monthlyUsage is not null && monthlyUsage != expectedMonthlyUsage)
            throw new ScheduleCsvException(field, $"Monthly R/R1 must be {expectedMonthlyUsage.Rest} and {expectedMonthlyUsage.SpecialRest} from the daily cells.");
        var expectedMonthlyLeaveRest = assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest);
        if (closingUsage is not null && monthlyLeaveRest != expectedMonthlyLeaveRest)
            throw new ScheduleCsvException(field, $"Monthly R休 must be {expectedMonthlyLeaveRest} from the daily cells.");
        return employee;
    }

    private static RestUsage CountRestUsage(IEnumerable<ScheduleCell> cells) => new(
        cells.Count(cell => cell.Kind == AssignmentKind.Rest),
        cells.Count(cell => cell.Kind == AssignmentKind.SpecialRest));

    private static ScheduleCell ParseCell(
        string text,
        DateOnly date,
        Shift? monthlyShift,
        IReadOnlyDictionary<string, NonStandardShift> nonStandardShifts,
        bool historical,
        string field) => text switch
    {
        "R" => new() { Kind = AssignmentKind.Rest },
        "R1" => new() { Kind = AssignmentKind.SpecialRest },
        "R休" => new() { Kind = AssignmentKind.LeaveRest },
        "R*" when historical => new() { Kind = AssignmentKind.Rest, RequestedRest = true },
        "R1*" when historical => new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
        "R休*" when historical => new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true },
        "R*" => new() { RequestedRest = true },
        "R*[R]" => new() { Kind = AssignmentKind.Rest, RequestedRest = true },
        "R*[R1]" => new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
        "R*[R休]" => new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true },
        _ when EventPattern().Match(text) is { Success: true } match => EventCell(match, date, field),
        _ when monthlyShift is not null && ShiftFromText(text) is { } shift => new() { Kind = AssignmentKind.Work, Shift = shift },
        _ when nonStandardShifts.GetValueOrDefault(text) is { } shift => EventCell(shift.StartTime, shift.EndTime, date),
        _ when MWorkCell(text) is { } cell => cell,
        _ => throw new ScheduleCsvException(field, $"Unsupported schedule cell '{text}'.")
    };

    private static ScheduleCell? ParseMPerpetualCell(string text, string field) => text switch
    {
        "" => null,
        "R" => new() { Kind = AssignmentKind.Rest },
        _ when MWorkCell(text) is { } cell => cell,
        _ => throw new ScheduleCsvException(field, $"Unsupported M perpetual-schedule cell '{text}'.")
    };

    private static ScheduleCell? MWorkCell(string text)
    {
        var match = MWorkPattern().Match(text);
        if (match.Success)
            return new() { Kind = AssignmentKind.Work, Station = match.Groups[1].Value, Shift = MShiftFromText(match.Groups[2].Value) };
        match = MWorkShortPattern().Match(text);
        return match.Success
            ? new() { Kind = AssignmentKind.Work, Station = $"LB{int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture):D2}", Shift = MShiftFromText(match.Groups[2].Value) }
            : null;
    }

    private static ScheduleCell EventCell(Match match, DateOnly date, string field)
    {
        if (!TimeOnly.TryParseExact(match.Groups[1].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(match.Groups[2].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            throw new ScheduleCsvException(field, "X time must use HH:mm-HH:mm.");
        return EventCell(startTime, endTime, date);
    }

    private static ScheduleCell EventCell(TimeOnly startTime, TimeOnly endTime, DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(startTime), TaipeiOffset);
        var end = new DateTimeOffset((endTime <= startTime ? date.AddDays(1) : date).ToDateTime(endTime), TaipeiOffset);
        return new() { Kind = AssignmentKind.WorkEvent, EventStart = start, EventEnd = end };
    }

    private static IReadOnlyDictionary<string, NonStandardShift> NonStandardShiftLookup(NonStandardShiftTable? table)
    {
        var result = new Dictionary<string, NonStandardShift>(StringComparer.Ordinal);
        foreach (var shift in table?.Shifts ?? [])
        {
            result[shift.Code] = shift;
            if (!string.IsNullOrWhiteSpace(shift.Name)) result[shift.Name] = shift;
        }
        return result;
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
            AssignmentKind.LeaveRest when cell.RequestedRest => "R*[R休]",
            AssignmentKind.Rest => "R",
            AssignmentKind.SpecialRest => "R1",
            AssignmentKind.LeaveRest => "R休",
            AssignmentKind.WorkEvent => $"X[{cell.EventStart:HH\\:mm}-{cell.EventEnd:HH\\:mm}]",
            AssignmentKind.Work when cell.Station is not null => cell.Station + MShiftText(cell.Shift),
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

    private static Shift? MShiftFromText(string text) => text == "小" ? Shift.Afternoon : ShiftFromText(text);

    private static string ShiftText(Shift? shift) => shift switch
    {
        Shift.Early => "早",
        Shift.Afternoon => "午",
        Shift.Night => "夜",
        _ => ""
    };

    private static string MShiftText(Shift? shift) => shift == Shift.Afternoon ? "小" : ShiftText(shift);

    private static string Join(IEnumerable<string> values) => string.Join(',', values.Select(Escape));
    private static string Escape(string value)
    {
        // Prevent spreadsheet programs from interpreting imported text as a formula.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    [GeneratedRegex(@"^X\[(\d{2}:\d{2})-(\d{2}:\d{2})\]$", RegexOptions.CultureInvariant)]
    private static partial Regex EventPattern();

    [GeneratedRegex(@"^(LB(?:0[1-9]|1[0-2]))(早|午|小|夜)$", RegexOptions.CultureInvariant)]
    private static partial Regex MWorkPattern();

    [GeneratedRegex(@"^([1-9]|1[0-2])(早|午|小|夜)$", RegexOptions.CultureInvariant)]
    private static partial Regex MWorkShortPattern();
}
