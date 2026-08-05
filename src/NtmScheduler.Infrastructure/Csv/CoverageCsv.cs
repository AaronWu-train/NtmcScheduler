using System.Globalization;
using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Infrastructure.Csv;

public static class CoverageCsv
{
    public static IReadOnlyList<MCoverageCsvRow> ReadM(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        Require(header, ["date", "location", "shift", "required", "assigned", "external", "unassigned"]);
        var list = new List<MCoverageCsvRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 7)
                throw new FormatException($"coverage.csv 第 {i + 2} 列欄位數不足");
            list.Add(new MCoverageCsvRow(
                ParseDate(row[0], i + 2),
                row[1].Trim(),
                row[2].Trim(),
                ParseInt(row[3], i + 2, "required"),
                ParseInt(row[4], i + 2, "assigned"),
                ParseInt(row[5], i + 2, "external"),
                ParseInt(row[6], i + 2, "unassigned")));
        }

        return list;
    }

    public static byte[] WriteM(IEnumerable<MCoverageCsvRow> rows)
    {
        var output = new List<IEnumerable<string?>>
        {
            new[] { "date", "location", "shift", "required", "assigned", "external", "unassigned" }
        };
        foreach (var r in rows)
        {
            output.Add([
                r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Location,
                r.Shift,
                r.Required.ToString(CultureInfo.InvariantCulture),
                r.Assigned.ToString(CultureInfo.InvariantCulture),
                r.External.ToString(CultureInfo.InvariantCulture),
                r.Unassigned.ToString(CultureInfo.InvariantCulture)
            ]);
        }

        return CsvWriter.WriteToBytes(output);
    }

    public static IReadOnlyList<TCoverageCsvRow> ReadT(Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        Require(header, [
            "date", "shift", "group_size", "normal_attend", "attend_target", "avg_ability", "missing_specialties"
        ]);
        var list = new List<TCoverageCsvRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 7)
                throw new FormatException($"t_coverage.csv 第 {i + 2} 列欄位數不足");
            var missing = string.IsNullOrWhiteSpace(row[6])
                ? Array.Empty<string>()
                : row[6].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            list.Add(new TCoverageCsvRow(
                ParseDate(row[0], i + 2),
                row[1].Trim(),
                ParseInt(row[2], i + 2, "group_size"),
                ParseInt(row[3], i + 2, "normal_attend"),
                ParseInt(row[4], i + 2, "attend_target"),
                decimal.Parse(row[5].Trim(), CultureInfo.InvariantCulture),
                missing));
        }

        return list;
    }

    public static byte[] WriteT(IEnumerable<TCoverageCsvRow> rows)
    {
        var output = new List<IEnumerable<string?>>
        {
            new[]
            {
                "date", "shift", "group_size", "normal_attend", "attend_target", "avg_ability",
                "missing_specialties"
            }
        };
        foreach (var r in rows)
        {
            output.Add([
                r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Shift,
                r.GroupSize.ToString(CultureInfo.InvariantCulture),
                r.NormalAttend.ToString(CultureInfo.InvariantCulture),
                r.AttendTarget.ToString(CultureInfo.InvariantCulture),
                r.AvgAbility.ToString(CultureInfo.InvariantCulture),
                string.Join('|', r.MissingSpecialties)
            ]);
        }

        return CsvWriter.WriteToBytes(output);
    }

    private static DateOnly ParseDate(string text, int row)
    {
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
            throw new FormatException($"coverage 第 {row} 列 date 無法解析：{text}");
        return d;
    }

    private static int ParseInt(string text, int row, string field)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"coverage 第 {row} 列 {field} 無法解析：{text}");
        return v;
    }

    private static void Require(IReadOnlyList<string> header, string[] expected)
    {
        if (header.Count < expected.Length)
            throw new FormatException("coverage CSV 欄位不足");
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(header[i], expected[i], StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"coverage CSV 預期欄位 {expected[i]}，實際為 {header[i]}");
        }
    }
}
