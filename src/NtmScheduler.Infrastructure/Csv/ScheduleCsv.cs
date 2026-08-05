using System.Globalization;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Csv;

public sealed record ScheduleCsvDocument(
    Unit Unit,
    /// <summary>Third column header: home_station (M) or shift (T).</summary>
    string ThirdColumnHeader,
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<ScheduleCsvRow> Rows);

public sealed record ScheduleCsvRow(
    string EmployeeId,
    string Name,
    string? ThirdColumnValue,
    /// <summary>Raw display states — R1 must remain "R1", never normalized to "R".</summary>
    IReadOnlyDictionary<DateOnly, string> DayStates,
    int MonthR,
    int MonthR1,
    int CycleR,
    int CycleR1);

public static class ScheduleCsv
{
    public const string HomeStationHeader = "home_station";
    public const string ShiftHeader = "shift";

    public static ScheduleCsvDocument Read(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        if (header.Count < 7)
            throw new FormatException("schedule.csv 欄位不足");

        if (!string.Equals(header[0], "employee_id", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("schedule.csv 缺少 employee_id");
        if (!string.Equals(header[1], "name", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("schedule.csv 缺少 name");

        var third = header[2];
        Unit unit;
        if (string.Equals(third, HomeStationHeader, StringComparison.OrdinalIgnoreCase))
            unit = Unit.M;
        else if (string.Equals(third, ShiftHeader, StringComparison.OrdinalIgnoreCase))
            unit = Unit.T;
        else
            throw new FormatException($"schedule.csv 第三欄必須為 home_station 或 shift，實際為：{third}");

        var statStart = header.Count - 4;
        ExpectHeader(header[statStart], "month_r");
        ExpectHeader(header[statStart + 1], "month_r1");
        ExpectHeader(header[statStart + 2], "cycle_r");
        ExpectHeader(header[statStart + 3], "cycle_r1");

        var dates = new List<DateOnly>();
        for (var i = 3; i < statStart; i++)
        {
            if (!DateOnly.TryParseExact(header[i], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                throw new FormatException($"schedule.csv 日期欄無法解析：{header[i]}");
            dates.Add(d);
        }

        var parsedRows = new List<ScheduleCsvRow>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count < header.Count)
                throw new FormatException($"schedule.csv 第 {r + 2} 列欄位數不足");

            var dayStates = new Dictionary<DateOnly, string>();
            for (var i = 0; i < dates.Count; i++)
            {
                // Preserve raw display text (R1 stays R1).
                var raw = row[3 + i].Trim();
                dayStates[dates[i]] = raw;
            }

            parsedRows.Add(new ScheduleCsvRow(
                row[0].Trim(),
                row[1].Trim(),
                string.IsNullOrWhiteSpace(row[2]) ? null : row[2].Trim(),
                dayStates,
                ParseInt(row[statStart], "month_r", r + 2),
                ParseInt(row[statStart + 1], "month_r1", r + 2),
                ParseInt(row[statStart + 2], "cycle_r", r + 2),
                ParseInt(row[statStart + 3], "cycle_r1", r + 2)));
        }

        return new ScheduleCsvDocument(unit, third, dates, parsedRows);
    }

    public static byte[] Write(ScheduleCsvDocument document)
    {
        var header = new List<string> { "employee_id", "name", document.ThirdColumnHeader };
        header.AddRange(document.Dates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        header.AddRange(["month_r", "month_r1", "cycle_r", "cycle_r1"]);

        var rows = new List<IEnumerable<string?>> { header };
        foreach (var row in document.Rows)
        {
            var cells = new List<string?>
            {
                row.EmployeeId,
                row.Name,
                row.ThirdColumnValue ?? ""
            };
            foreach (var date in document.Dates)
            {
                if (!row.DayStates.TryGetValue(date, out var state))
                    throw new InvalidOperationException($"缺少 {row.EmployeeId} @ {date} 狀態");
                // Write raw display — do not rewrite R1 → R.
                cells.Add(state);
            }

            cells.Add(row.MonthR.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.MonthR1.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CycleR.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.CycleR1.ToString(CultureInfo.InvariantCulture));
            rows.Add(cells);
        }

        return CsvWriter.WriteToBytes(rows);
    }

    /// <summary>Infer target month as the YearMonth covering the most date columns.</summary>
    public static YearMonth InferTargetMonth(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0)
            throw new InvalidOperationException("schedule.csv 無日期欄");

        return dates
            .GroupBy(d => new YearMonth(d.Year, d.Month))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }

    private static void ExpectHeader(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"schedule.csv 預期欄位 {expected}，實際為 {actual}");
    }

    private static int ParseInt(string text, string field, int row)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"schedule.csv 第 {row} 列 {field} 無法解析：{text}");
        return v;
    }
}
