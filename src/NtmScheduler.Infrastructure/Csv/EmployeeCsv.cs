using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Csv;

public static class EmployeeCsv
{
    public static IReadOnlyList<EmployeeInfo> Read(Unit unit, Stream stream)
    {
        var (header, rows) = CsvReader.ReadTable(stream);
        return unit == Unit.M ? ReadM(header, rows) : ReadT(header, rows);
    }

    public static byte[] Write(Unit unit, IEnumerable<EmployeeInfo> employees)
    {
        return unit == Unit.M ? WriteM(employees) : WriteT(employees);
    }

    private static IReadOnlyList<EmployeeInfo> ReadM(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Require(header, ["employee_id", "name", "home_station"]);
        var list = new List<EmployeeInfo>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 3)
                throw new FormatException($"m_employees.csv 第 {i + 2} 列欄位數不足");
            var id = row[0].Trim();
            var name = row[1].Trim();
            var station = row[2].Trim();
            if (string.IsNullOrEmpty(id))
                throw new FormatException($"m_employees.csv 第 {i + 2} 列 employee_id 空白");
            list.Add(new EmployeeInfo(id, name, Unit.M, HomeStation: station));
        }

        return list;
    }

    private static IReadOnlyList<EmployeeInfo> ReadT(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Require(header, ["employee_id", "name", "specialty", "ability"]);
        var list = new List<EmployeeInfo>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count < 4)
                throw new FormatException($"t_employees.csv 第 {i + 2} 列欄位數不足");
            var id = row[0].Trim();
            var name = row[1].Trim();
            var specialty = string.IsNullOrWhiteSpace(row[2]) ? null : row[2].Trim();
            var abilityText = row[3].Trim();
            if (string.IsNullOrEmpty(id))
                throw new FormatException($"t_employees.csv 第 {i + 2} 列 employee_id 空白");
            if (!int.TryParse(abilityText, out var ability) || ability is < 1 or > 5)
                throw new FormatException($"t_employees.csv 第 {i + 2} 列 ability 必須為 1–5：{abilityText}");
            list.Add(new EmployeeInfo(id, name, Unit.T, Specialty: specialty, Ability: ability));
        }

        return list;
    }

    private static byte[] WriteM(IEnumerable<EmployeeInfo> employees)
    {
        var rows = new List<IEnumerable<string?>>
        {
            new[] { "employee_id", "name", "home_station" }
        };
        foreach (var e in employees)
            rows.Add([e.Id, e.Name, e.HomeStation ?? ""]);
        return CsvWriter.WriteToBytes(rows);
    }

    private static byte[] WriteT(IEnumerable<EmployeeInfo> employees)
    {
        var rows = new List<IEnumerable<string?>>
        {
            new[] { "employee_id", "name", "specialty", "ability" }
        };
        foreach (var e in employees)
            rows.Add([e.Id, e.Name, e.Specialty ?? "", e.Ability?.ToString() ?? ""]);
        return CsvWriter.WriteToBytes(rows);
    }

    private static void Require(IReadOnlyList<string> header, string[] expected)
    {
        if (header.Count < expected.Length)
            throw new FormatException("員工 CSV 欄位不足");
        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(header[i], expected[i], StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"員工 CSV 預期欄位 {expected[i]}，實際為 {header[i]}");
        }
    }
}
