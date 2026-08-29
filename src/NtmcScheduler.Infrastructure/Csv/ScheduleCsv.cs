using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using NtmcScheduler.Contracts;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Csv;

public sealed class ScheduleCsvException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Converts the stable monthly-schedule CSV boundary to and from solver contracts.</summary>
public static partial class ScheduleCsv
{
    private static readonly string[] LegacyHeaders =
    [
        "ID", "姓名", "所屬", "月中開始排班日", "月中排班終止日", "能力", "T月班別",
        "月初區間累計R", "月初區間累計R1",
        .. Enumerable.Range(1, 31).Select(day => day.ToString(CultureInfo.InvariantCulture)),
        "當月R", "當月R1", "R休下限", "R休上限", "月底區間累計R", "月底區間累計R1", "本月班數"
    ];
    private static readonly string[] Headers = [.. LegacyHeaders, "萬年班表"];

    private static readonly string[] OldLegacyHeaders =
        LegacyHeaders.Where(x => x is not "R休下限" and not "月中排班終止日").ToArray();
    private static readonly string[] OldHeaders = [.. OldLegacyHeaders, "萬年班表"];
    private static readonly string[] PreviousLegacyHeaders = LegacyHeaders.Where(x => x != "月中排班終止日").ToArray();
    private static readonly string[] PreviousHeaders = [.. PreviousLegacyHeaders, "萬年班表"];

    public static string MonthlyHeader => Join(Headers);
    public static IReadOnlyList<string> MonthlyHeaders { get; } = Array.AsReadOnly(Headers);
    public static string MonthlyDownloadHeader(WorkspaceCode workspace) => Join(MonthlyDownloadHeaders(workspace));
    public static IReadOnlyList<string> MonthlyDownloadHeaders(WorkspaceCode workspace) =>
        Headers.Where(header => !IsExcludedFromDownload(header, workspace)).ToArray();
    public static IReadOnlyList<string> MonthlyTemplateHeaders(WorkspaceCode workspace, bool historical) =>
        Headers.Where(header => !IsExcludedFromTemplate(header, workspace, historical)).ToArray();
    public static string MPerpetualHeader => Join(["萬年班表", .. Enumerable.Range(1, 56).Select(day => day.ToString(CultureInfo.InvariantCulture))]);

    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);

    public static MonthlySchedule ReadMonthly(string path, DateOnly monthStart, NonStandardShiftTable? nonStandardShifts = null, bool historical = false,
        WorkspaceCode workspace = WorkspaceCode.M, bool ignoreDerivedHistoricalFields = false)
    {
        if (monthStart.Day != 1) throw new ScheduleCsvException(nameof(monthStart), "Month must start on day one.");
        var rows = ReadRows(path);
        if (rows.Count == 0) throw new ScheduleCsvException(path, "CSV is empty.");
        var header = IgnoreTrailingEmptyFields(rows[0], Headers.Length);
        if (!TryResolveHeaderFormat(header, workspace, historical, out var sourceHeaders))
            throw new ScheduleCsvException(path, "Monthly schedule headers do not match the required format.");
        var hasPerpetualSchedule = sourceHeaders.Contains("萬年班表");
        var fieldCount = sourceHeaders.Count;
        var nonStandardShiftLookup = NonStandardShiftLookup(nonStandardShifts);

        var employees = new List<EmployeeMonthlySchedule>();
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            row = IgnoreTrailingEmptyFields(row, fieldCount);
            if (row.Length != fieldCount) throw new ScheduleCsvException($"{path}:{rowNumber + 1}", $"Expected {fieldCount} fields but found {row.Length}.");
            employees.Add(ParseEmployee(NormalizeMonthlyRow(sourceHeaders, row), rowNumber + 1, monthStart, nonStandardShiftLookup,
                historical, hasPerpetualSchedule, workspace, ignoreDerivedHistoricalFields));
        }
        return new(monthStart, employees);
    }

    public static void WriteMonthly(string path, MonthlySchedule schedule)
        => File.WriteAllBytes(path, WriteMonthly(schedule));

    public static byte[] WriteMonthly(MonthlySchedule schedule)
    {
        var lines = new List<string> { Join(Headers) };
        foreach (var employee in schedule.Employees)
            lines.Add(Join(MonthlyRow(schedule, employee)));
        return Encoding.UTF8.GetBytes('\uFEFF' + string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static byte[] WriteMonthlyDownload(MonthlySchedule schedule, WorkspaceCode workspace)
    {
        var excludedColumns = ExcludedDownloadColumnIndexes(workspace);
        var lines = new List<string> { MonthlyDownloadHeader(workspace) };
        foreach (var employee in schedule.Employees)
            lines.Add(Join(MonthlyRow(schedule, employee).Where((_, index) => !excludedColumns.Contains(index))));
        return Encoding.UTF8.GetBytes('\uFEFF' + string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static byte[] WriteMonthlyTemplate(MonthlySchedule schedule, WorkspaceCode workspace, bool historical)
    {
        var included = MonthlyTemplateHeaders(workspace, historical).ToHashSet(StringComparer.Ordinal);
        var indexes = Headers.Select((header, index) => (header, index)).Where(x => included.Contains(x.header)).Select(x => x.index).ToArray();
        var lines = new List<string> { Join(indexes.Select(index => Headers[index])) };
        foreach (var employee in schedule.Employees)
        {
            var row = MonthlyRow(schedule, employee);
            lines.Add(Join(indexes.Select(index => row[index])));
        }
        return Encoding.UTF8.GetBytes('\uFEFF' + string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static IReadOnlyList<string> MonthlyRow(MonthlySchedule schedule, EmployeeMonthlySchedule employee)
    {
        var values = new List<string>
        {
            employee.EmployeeId,
            employee.Name,
            employee.Affiliation,
            employee.EmploymentStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            employee.EmploymentEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            employee.Ability?.ToString(CultureInfo.InvariantCulture) ?? "",
            ShiftText(employee.MonthlyShift),
            employee.OpeningUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "",
            employee.OpeningUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? ""
        };
        values.AddRange(Enumerable.Range(1, 31).Select(day => CellText(schedule, employee, day)));
        var completed = employee.ClosingUsage is not null || employee.NormalWorkCount is not null;
        var monthlyUsage = completed ? CountRestUsage(employee.Assignments.Values) : null;
        values.Add(monthlyUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add(monthlyUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add((completed ? null : employee.RequestedLeaveRestMinimum)?.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add((completed
            ? employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest)
            : employee.RequestedLeaveRestCount)?.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add(employee.ClosingUsage?.Rest.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add(employee.ClosingUsage?.SpecialRest.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add(employee.NormalWorkCount?.ToString(CultureInfo.InvariantCulture) ?? "");
        values.Add(employee.PerpetualScheduleId ?? "");
        return values;
    }

    public static IReadOnlyList<RestInterval> ReadRestIntervals(string path)
    {
        var rows = ReadRows(path);
        string[] expected = ["區間開始日期", "區間結束日期", "國定假日日期"];
        if (rows.Count == 0 || !IgnoreTrailingEmptyFields(rows[0], expected.Length).SequenceEqual(expected))
            throw new ScheduleCsvException(path, "Rest-interval headers do not match the required format.");

        var intervals = new List<RestInterval>();
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            row = IgnoreTrailingEmptyFields(row, expected.Length);
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
        if (rows.Count == 0 || !IgnoreTrailingEmptyFields(rows[0], expected.Length).SequenceEqual(expected))
            throw new ScheduleCsvException(path, "非常態班型 CSV 表頭不符合規定。");

        var shifts = new List<NonStandardShift>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            row = IgnoreTrailingEmptyFields(row, expected.Length);
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
            var names = NonStandardShiftNames(name);
            if (!tokens.Add(code) || IsReservedShiftName(code) || names.Any(alias => !tokens.Add(alias)) || names.Any(IsReservedShiftName))
                throw new ScheduleCsvException(field, "非常態班型名稱與代碼不可重複，且不可使用早、午、小、夜。");
            shifts.Add(new(name, startTime, endTime, code));
        }
        return new(shifts);
    }

    public static MPerpetualSchedule ReadMPerpetualSchedule(string path, WorkspaceCode workspace = WorkspaceCode.M)
    {
        var rows = ReadRows(path);
        string[] expected = ["萬年班表", .. Enumerable.Range(1, 56).Select(day => day.ToString(CultureInfo.InvariantCulture))];
        if (rows.Count == 0 || !IgnoreTrailingEmptyFields(rows[0], expected.Length).SequenceEqual(expected))
            throw new ScheduleCsvException(path, "M perpetual-schedule headers do not match the required format.");

        var patterns = new Dictionary<string, IReadOnlyList<ScheduleCell?>>(StringComparer.Ordinal);
        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            row = IgnoreTrailingEmptyFields(row, expected.Length);
            var field = $"{path}:{rowNumber + 1}";
            if (row.Length != expected.Length) throw new ScheduleCsvException(field, $"Expected {expected.Length} fields but found {row.Length}.");
            var id = row[0].Trim();
            if (id.Length == 0) throw new ScheduleCsvException(field, "萬年班表代號不可空白。");
            if (!patterns.TryAdd(id, row.Skip(1).Select((text, index) => ParseMPerpetualCell(text.Trim(), $"{field} day {index + 1}", workspace)).ToArray()))
                throw new ScheduleCsvException(field, $"萬年班表代號 '{id}' 不可重複。");
        }
        return new(patterns);
    }

    public static byte[] WriteMPerpetualSchedule(MPerpetualSchedule schedule, WorkspaceCode workspace = WorkspaceCode.M)
    {
        var lines = new List<string> { MPerpetualHeader };
        foreach (var pattern in schedule.Patterns.OrderBy(x => x.Key, StringComparer.Ordinal))
            lines.Add(Join([pattern.Key, .. pattern.Value.Select(cell => MPerpetualCellText(cell, workspace))]));
        return new UTF8Encoding(true).GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static EmployeeMonthlySchedule ParseEmployee(
        string[] row,
        int rowNumber,
        DateOnly monthStart,
        IReadOnlyDictionary<string, NonStandardShift> nonStandardShifts,
        bool historical,
        bool hasPerpetualSchedule,
        WorkspaceCode workspace,
        bool ignoreDerivedHistoricalFields)
    {
        var field = $"row {rowNumber}";
        var ability = ignoreDerivedHistoricalFields ? null : NullableInt(row[5], $"{field} 能力");
        var monthlyShift = NullableShift(row[6], $"{field} T月班別");
        var monthlyUsage = ignoreDerivedHistoricalFields ? null : Usage(row[40], row[41], $"{field} monthly");
        int? monthlyLeaveRestMinimum = ignoreDerivedHistoricalFields ? null : NullableInt(row[42], $"{field} R休下限") ?? 0;
        var monthlyLeaveRest = ignoreDerivedHistoricalFields ? null : NullableInt(row[43], $"{field} R休上限");
        var closingUsage = ignoreDerivedHistoricalFields ? null : Usage(row[44], row[45], $"{field} closing");
        var employee = new EmployeeMonthlySchedule
        {
            EmployeeId = row[0].Trim(),
            Name = row[1].Trim(),
            Affiliation = row[2].Trim(),
            EmploymentStartDate = NullableDate(row[3], $"{field} 月中開始排班日"),
            EmploymentEndDate = NullableDate(row[4], $"{field} 月中排班終止日"),
            Ability = ability,
            MonthlyShift = monthlyShift,
            PerpetualScheduleId = hasPerpetualSchedule && !string.IsNullOrWhiteSpace(row[47]) ? row[47].Trim() : null,
            RequestedLeaveRestMinimum = closingUsage is null ? monthlyLeaveRestMinimum : null,
            RequestedLeaveRestCount = closingUsage is null ? monthlyLeaveRest : null,
            OpeningUsage = Usage(row[7], row[8], $"{field} opening"),
            Assignments = new Dictionary<DateOnly, ScheduleCell>(),
            ClosingUsage = closingUsage,
            NormalWorkCount = ignoreDerivedHistoricalFields ? null : NullableInt(row[46], $"{field} 本月班數")
        };

        var assignments = (Dictionary<DateOnly, ScheduleCell>)employee.Assignments;
        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        for (var day = 1; day <= 31; day++)
        {
            var text = row[8 + day].Trim();
            if (day > days)
            {
                if (text.Length > 0) throw new ScheduleCsvException($"{field} day {day}", "A non-existent calendar day must be blank.");
                continue;
            }
            if (text.Length == 0) continue;
            var date = monthStart.AddDays(day - 1);
            if (employee.EmploymentStartDate is { } employmentStart && date < employmentStart || employee.EmploymentEndDate is { } employmentEnd && date > employmentEnd)
                throw new ScheduleCsvException($"{field} day {day}", "A schedule cell outside the employee scheduling range must be blank.");
            assignments[date] = ParseCell(text, date, monthlyShift, nonStandardShifts, historical || closingUsage is not null, $"{field} day {day}", workspace);
        }
        var expectedMonthlyUsage = CountRestUsage(assignments.Values);
        if (!ignoreDerivedHistoricalFields)
        {
            if (closingUsage is null && monthlyUsage is not null)
                throw new ScheduleCsvException(field, "Monthly R/R1 must be blank when closing interval totals are blank.");
            if (closingUsage is not null && monthlyUsage is null)
                throw new ScheduleCsvException(field, "Monthly R/R1 is required when closing interval totals are filled.");
            if (monthlyUsage is not null && monthlyUsage != expectedMonthlyUsage)
                throw new ScheduleCsvException(field, $"Monthly R/R1 must be {expectedMonthlyUsage.Rest} and {expectedMonthlyUsage.SpecialRest} from the daily cells.");
        }
        var expectedMonthlyLeaveRest = assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest);
        if (!ignoreDerivedHistoricalFields && closingUsage is not null && monthlyLeaveRest != expectedMonthlyLeaveRest)
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
        string field,
        WorkspaceCode workspace) => text switch
        {
            "R" => new() { Kind = AssignmentKind.Rest },
            "R1" => new() { Kind = AssignmentKind.SpecialRest },
            "R休" => new() { Kind = AssignmentKind.LeaveRest },
            "R休*" => new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true },
            "R*" when historical => new() { Kind = AssignmentKind.Rest, RequestedRest = true },
            "R1*" when historical => new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
            "R*" => new() { RequestedRest = true },
            "R*[R]" => new() { Kind = AssignmentKind.Rest, RequestedRest = true },
            "R*[R1]" => new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
            "R*[R休]" => new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true },
            _ when TryParseEventCell(text, date, field, out var eventCell) => eventCell,
            _ when monthlyShift is not null && ShiftFromText(text) is { } shift => new() { Kind = AssignmentKind.Work, Shift = shift },
            _ when nonStandardShifts.GetValueOrDefault(text) is { } shift => EventCell(shift.StartTime, shift.EndTime, date,
                workspace is WorkspaceCode.YM or WorkspaceCode.YT ? $"{shift.Name ?? shift.Code}({shift.Code})" : shift.Name ?? shift.Code),
            _ when MWorkCell(text, workspace) is { } cell => cell,
            _ => throw new ScheduleCsvException(field, $"Unsupported schedule cell '{text}'.")
        };

    internal static ScheduleCell? ParseMPerpetualCell(string text, string field, WorkspaceCode workspace = WorkspaceCode.M) => text switch
    {
        "" => null,
        "R" => new() { Kind = AssignmentKind.Rest },
        _ when MWorkCell(text, workspace) is { } cell => cell,
        _ => throw new ScheduleCsvException(field, $"Unsupported M perpetual-schedule cell '{text}'.")
    };

    internal static string MPerpetualCellText(ScheduleCell? cell, WorkspaceCode workspace = WorkspaceCode.M) => cell?.Kind switch
    {
        null => "",
        AssignmentKind.Rest => "R",
        AssignmentKind.Work when cell.Station is not null && workspace.Stations().Contains(cell.Station, StringComparer.Ordinal) => MWorkCellText(cell.Station, cell.Shift),
        _ => throw new ScheduleCsvException(nameof(MPerpetualSchedule), "M perpetual schedule contains an unsupported cell.")
    };

    private static ScheduleCell? MWorkCell(string text, WorkspaceCode workspace)
    {
        var match = MWorkPattern().Match(text);
        if (match.Success && workspace.Stations().Contains(match.Groups[1].Value, StringComparer.Ordinal))
            return new() { Kind = AssignmentKind.Work, Station = match.Groups[1].Value, Shift = MShiftFromText(match.Groups[2].Value) };
        match = MWorkShortPattern().Match(text);
        if (!match.Success) return null;
        var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var station = workspace == WorkspaceCode.YM ? $"Y{number:D2}" : $"LB{number:D2}";
        return workspace.Stations().Contains(station, StringComparer.Ordinal)
            ? new() { Kind = AssignmentKind.Work, Station = station, Shift = MShiftFromText(match.Groups[2].Value) }
            : null;
    }

    private static ScheduleCell EventCell(TimeOnly startTime, TimeOnly endTime, DateOnly date, string? eventDescription = null)
    {
        var start = new DateTimeOffset(date.ToDateTime(startTime), TaipeiOffset);
        var end = new DateTimeOffset((endTime <= startTime ? date.AddDays(1) : date).ToDateTime(endTime), TaipeiOffset);
        return new() { Kind = AssignmentKind.WorkEvent, EventStart = start, EventEnd = end, EventDescription = NormalizeEventDescription(eventDescription, null) };
    }

    private static bool TryParseEventCell(string text, DateOnly date, string field, out ScheduleCell cell)
    {
        cell = null!;
        if (!text.StartsWith("X[", StringComparison.Ordinal) || !text.EndsWith(']')) return false;

        var body = text[2..^1];
        var separatorIndex = body.IndexOf('|');
        var timePart = separatorIndex >= 0 ? body[..separatorIndex] : body;
        var descriptionPart = separatorIndex >= 0 ? body[(separatorIndex + 1)..] : null;
        var dashIndex = timePart.IndexOf('-');
        if (dashIndex <= 0 || dashIndex >= timePart.Length - 1)
            throw new ScheduleCsvException(field, "X time must use HH:mm-HH:mm.");
        if (!TimeOnly.TryParseExact(timePart[..dashIndex], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(timePart[(dashIndex + 1)..], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            throw new ScheduleCsvException(field, "X time must use HH:mm-HH:mm.");

        cell = EventCell(startTime, endTime, date, NormalizeEventDescription(descriptionPart, field));
        return true;
    }

    private static IReadOnlyDictionary<string, NonStandardShift> NonStandardShiftLookup(NonStandardShiftTable? table)
    {
        var result = new Dictionary<string, NonStandardShift>(StringComparer.Ordinal);
        foreach (var shift in table?.Shifts ?? [])
        {
            result[shift.Code] = shift with { Name = shift.Code };
            foreach (var name in NonStandardShiftNames(shift.Name))
            {
                result[name] = shift with { Name = name };
                result[$"{name}({shift.Code})"] = shift with { Name = name };
            }
        }
        return result;
    }

    private static string CellText(MonthlySchedule schedule, EmployeeMonthlySchedule employee, int day)
    {
        if (day > DateTime.DaysInMonth(schedule.MonthStart.Year, schedule.MonthStart.Month)) return "";
        var date = schedule.MonthStart.AddDays(day - 1);
        if ((employee.EmploymentStartDate is { } start && date < start) ||
            (employee.EmploymentEndDate is { } end && date > end) ||
            !employee.Assignments.TryGetValue(date, out var cell)) return "";
        return cell.Kind switch
        {
            null when cell.RequestedRest => "R*",
            AssignmentKind.Rest when cell.RequestedRest => "R*",
            AssignmentKind.SpecialRest when cell.RequestedRest => "R1*",
            AssignmentKind.LeaveRest when cell.RequestedRest => "R休*",
            AssignmentKind.Rest => "R",
            AssignmentKind.SpecialRest => "R1",
            AssignmentKind.LeaveRest => "R休",
            AssignmentKind.WorkEvent => EventCellText(cell),
            AssignmentKind.Work when cell.Station is not null => MWorkCellText(cell.Station, cell.Shift),
            AssignmentKind.Work => ShiftText(cell.Shift),
            _ => ""
        };
    }

    private static string EventCellText(ScheduleCell cell)
    {
        var text = $"X[{cell.EventStart:HH\\:mm}-{cell.EventEnd:HH\\:mm}";
        var description = NormalizeEventDescription(cell.EventDescription, null);
        return description is null ? text + "]" : $"{text}|{description}]";
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

    private static string[] IgnoreTrailingEmptyFields(string[] fields, int expectedCount) =>
        fields.Length > expectedCount && fields.Skip(expectedCount).All(string.IsNullOrWhiteSpace)
            ? fields[..expectedCount]
            : fields;

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

    private static string MWorkCellText(string station, Shift? shift) =>
        ((station.Length == 4 && station.StartsWith("LB", StringComparison.Ordinal) &&
          int.TryParse(station.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 12) ||
         (station.Length == 3 && station.StartsWith('Y') &&
          int.TryParse(station.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number is >= 6 and <= 19))
            ? number.ToString(CultureInfo.InvariantCulture) + MShiftText(shift)
            : station + MShiftText(shift);

    private static bool IsExcludedFromDownload(string header, WorkspaceCode workspace) => workspace switch
    {
        WorkspaceCode.M or WorkspaceCode.YM => header is "能力" or "T月班別",
        WorkspaceCode.T or WorkspaceCode.YT => header is "萬年班表",
        _ => throw new ArgumentOutOfRangeException(nameof(workspace))
    };

    private static bool IsExcludedFromTemplate(string header, WorkspaceCode workspace, bool historical) =>
        IsExcludedFromDownload(header, workspace) ||
        historical && header is "能力" or "當月R" or "當月R1" or "R休下限" or "R休上限" or "月底區間累計R" or "月底區間累計R1" or "本月班數" ||
        !historical && header is "當月R" or "當月R1" or "月底區間累計R" or "月底區間累計R1" or "本月班數";

    private static HashSet<int> ExcludedDownloadColumnIndexes(WorkspaceCode workspace) =>
        Headers.Select((header, index) => (header, index))
            .Where(x => IsExcludedFromDownload(x.header, workspace))
            .Select(x => x.index)
            .ToHashSet();

    private static bool TryResolveHeaderFormat(string[] header, WorkspaceCode workspace, bool historical, out IReadOnlyList<string> sourceHeaders)
    {
        header = IgnoreTrailingEmptyFields(header, Headers.Length);
        if (header.Length > 3 && header[3] == "到職日期") header[3] = "月中開始排班日";
        for (var index = 0; index < header.Length; index++)
            header[index] = header[index] switch
            {
                "當月指定R休下界" => "R休下限",
                "當月指定R休" => "R休上限",
                _ => header[index]
            };
        if (header.SequenceEqual(Headers))
        {
            sourceHeaders = Headers;
            return true;
        }
        if (header.SequenceEqual(LegacyHeaders))
        {
            sourceHeaders = LegacyHeaders;
            return true;
        }
        if (header.SequenceEqual(PreviousHeaders) || header.SequenceEqual(PreviousLegacyHeaders))
        {
            sourceHeaders = header.SequenceEqual(PreviousHeaders) ? PreviousHeaders : PreviousLegacyHeaders;
            return true;
        }
        if (header.SequenceEqual(OldHeaders) || header.SequenceEqual(OldLegacyHeaders))
        {
            sourceHeaders = header.SequenceEqual(OldHeaders) ? OldHeaders : OldLegacyHeaders;
            return true;
        }
        var mDownloadHeaders = MonthlyDownloadHeaders(WorkspaceCode.M);
        if (header.SequenceEqual(mDownloadHeaders))
        {
            sourceHeaders = mDownloadHeaders;
            return true;
        }
        var templateHeaders = MonthlyTemplateHeaders(workspace, historical);
        if (header.SequenceEqual(templateHeaders))
        {
            sourceHeaders = templateHeaders;
            return true;
        }
        var required = new HashSet<string>(["ID", "姓名", "所屬", .. Enumerable.Range(1, 31).Select(x => x.ToString(CultureInfo.InvariantCulture))], StringComparer.Ordinal);
        if (workspace.IsMaintenance())
        {
            required.Add("T月班別");
            if (!historical) required.Add("能力");
        }
        var supplied = header.ToHashSet(StringComparer.Ordinal);
        if (supplied.Count == header.Length && required.IsSubsetOf(supplied) &&
            header.SequenceEqual(Headers.Where(supplied.Contains)))
        {
            sourceHeaders = header;
            return true;
        }
        sourceHeaders = Array.Empty<string>();
        return false;
    }

    private static string[] NormalizeMonthlyRow(IReadOnlyList<string> sourceHeaders, string[] row)
    {
        var values = Headers.ToDictionary(header => header, _ => "");
        for (var index = 0; index < sourceHeaders.Count && index < row.Length; index++)
            values[sourceHeaders[index]] = row[index];
        return Headers.Select(header => values[header]).ToArray();
    }

    private static string Join(IEnumerable<string> values) => string.Join(',', values.Select(Escape));
    private static string Escape(string value)
    {
        // Prevent spreadsheet programs from interpreting imported text as a formula.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string? NormalizeEventDescription(string? description, string? field)
    {
        if (description is null) return null;
        var trimmed = description.Trim();
        if (trimmed.Length == 0)
        {
            if (field is not null) throw new ScheduleCsvException(field, "X annotation cannot be blank.");
            return null;
        }
        if (trimmed.Length > 500)
        {
            if (field is not null) throw new ScheduleCsvException(field, "X annotation cannot exceed 500 characters.");
            return trimmed[..500];
        }
        return trimmed;
    }

    internal static IReadOnlyList<string> NonStandardShiftNames(string? names) =>
        string.IsNullOrWhiteSpace(names)
            ? []
            : names.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static bool IsReservedShiftName(string name) =>
        name is "R" or "R1" or "R休" or "R休*" or "R*" or "R1*" or "R*[R]" or "R*[R1]" or "R*[R休]" or "早" or "午" or "小" or "夜" ||
        name.StartsWith("X[", StringComparison.Ordinal) || MWorkPattern().IsMatch(name) || MWorkShortPattern().IsMatch(name);

    [GeneratedRegex(@"^(LB(?:0[1-9]|1[0-2])|Y(?:0[6-9]|1[0-9]))(早|午|小|夜)$", RegexOptions.CultureInvariant)]
    private static partial Regex MWorkPattern();

    [GeneratedRegex(@"^([1-9]|1[0-9])(早|午|小|夜)(?:SM)?$", RegexOptions.CultureInvariant)]
    private static partial Regex MWorkShortPattern();
}
