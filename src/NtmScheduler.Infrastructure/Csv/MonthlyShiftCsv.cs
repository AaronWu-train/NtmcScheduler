using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Csv;

public sealed record MonthlyShiftCsvRow(string EmployeeId, YearMonth Month, ShiftType Shift);

public static class MonthlyShiftCsv
{
    public static IReadOnlyList<MonthlyShiftCsvRow> Read(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        if (header.Count < 3
            || !string.Equals(header[0], "employee_id", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(header[1], "month", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(header[2], "shift", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("t_monthly_shift.csv 欄位必須為 employee_id,month,shift");

        var list = new List<MonthlyShiftCsvRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 3)
                throw new FormatException($"t_monthly_shift.csv 第 {i + 2} 列欄位數不足");
            list.Add(new MonthlyShiftCsvRow(
                row[0].Trim(),
                YearMonth.Parse(row[1].Trim()),
                ShiftTypeExtensions.ParseDisplay(row[2].Trim())));
        }

        return list;
    }

    public static byte[] Write(IEnumerable<MonthlyShiftCsvRow> rows)
    {
        var output = new List<IEnumerable<string?>>
        {
            new[] { "employee_id", "month", "shift" }
        };
        foreach (var r in rows)
            output.Add([r.EmployeeId, r.Month.ToString(), r.Shift.ToDisplay()]);
        return CsvWriter.WriteToBytes(output);
    }
}
