using System.Globalization;
using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Infrastructure.Csv;

public static class ViolationsCsv
{
    public static IReadOnlyList<ViolationCsvRow> Read(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        Require(header, ["solution_id", "rule_id", "date", "employee_id", "message"]);
        var list = new List<ViolationCsvRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 5)
                throw new FormatException($"violations.csv 第 {i + 2} 列欄位數不足");

            DateOnly? date = null;
            if (!string.IsNullOrWhiteSpace(row[2]))
            {
                if (!DateOnly.TryParseExact(row[2].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d))
                    throw new FormatException($"violations.csv 第 {i + 2} 列 date 無法解析：{row[2]}");
                date = d;
            }

            list.Add(new ViolationCsvRow(
                row[0].Trim(),
                row[1].Trim(),
                date,
                string.IsNullOrWhiteSpace(row[3]) ? null : row[3].Trim(),
                row[4]));
        }

        return list;
    }

    public static byte[] Write(IEnumerable<ViolationCsvRow> rows)
    {
        var output = new List<IEnumerable<string?>>
        {
            new[] { "solution_id", "rule_id", "date", "employee_id", "message" }
        };
        foreach (var r in rows)
        {
            output.Add([
                r.SolutionId,
                r.RuleId,
                r.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                r.EmployeeId ?? "",
                r.Message
            ]);
        }

        return CsvWriter.WriteToBytes(output);
    }

    private static void Require(IReadOnlyList<string> header, string[] expected)
    {
        if (header.Count < expected.Length)
            throw new FormatException("violations.csv 欄位不足");
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(header[i], expected[i], StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"violations.csv 預期欄位 {expected[i]}，實際為 {header[i]}");
        }
    }
}
