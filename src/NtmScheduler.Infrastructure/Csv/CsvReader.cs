using System.Text;

namespace NtmScheduler.Infrastructure.Csv;

/// <summary>
/// Minimal RFC-style CSV reader: UTF-8 (with/without BOM), quoted fields, commas inside quotes.
/// </summary>
public static class CsvReader
{
    public static IReadOnlyList<IReadOnlyList<string>> ReadAllRows(Stream stream)
    {
        using var reader = CreateTextReader(stream);
        var rows = new List<IReadOnlyList<string>>();
        while (true)
        {
            var row = ReadRow(reader);
            if (row is null)
                break;
            // Skip completely empty trailing lines
            if (row.Count == 1 && row[0].Length == 0 && reader.Peek() < 0 && rows.Count > 0)
                break;
            rows.Add(row);
        }

        return rows;
    }

    public static (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) ReadTable(Stream stream)
    {
        var all = ReadAllRows(stream);
        if (all.Count == 0)
            throw new FormatException("CSV 為空");

        var header = all[0];
        var rows = all.Skip(1).Where(r => r.Count > 0 && !(r.Count == 1 && r[0].Length == 0)).ToList();
        return (header, rows);
    }

    public static StreamReader CreateTextReader(Stream stream)
    {
        // Detect UTF-8 BOM; leave stream at first content byte.
        if (stream.CanSeek)
            stream.Position = 0;

        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    }

    private static List<string>? ReadRow(TextReader reader)
    {
        if (reader.Peek() < 0)
            return null;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                fields.Add(field.ToString());
                return fields;
            }

            var c = (char)next;
            if (inQuotes)
            {
                if (c == '"')
                {
                    var peek = reader.Peek();
                    if (peek == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        if (reader.Peek() == '\n')
                            reader.Read();
                        fields.Add(field.ToString());
                        return fields;
                    case '\n':
                        fields.Add(field.ToString());
                        return fields;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }
    }
}
