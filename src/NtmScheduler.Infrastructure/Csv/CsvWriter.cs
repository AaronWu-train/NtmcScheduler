using System.Text;

namespace NtmScheduler.Infrastructure.Csv;

/// <summary>
/// Minimal CSV writer: UTF-8 with BOM, quotes fields that need escaping.
/// </summary>
public static class CsvWriter
{
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static void WriteAll(Stream stream, IEnumerable<IEnumerable<string?>> rows)
    {
        using var writer = new StreamWriter(stream, Utf8Bom, leaveOpen: true);
        foreach (var row in rows)
            WriteRow(writer, row);
        writer.Flush();
    }

    public static byte[] WriteToBytes(IEnumerable<IEnumerable<string?>> rows)
    {
        using var ms = new MemoryStream();
        WriteAll(ms, rows);
        return ms.ToArray();
    }

    public static void WriteRow(TextWriter writer, IEnumerable<string?> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
                writer.Write(',');
            first = false;
            writer.Write(Escape(field ?? ""));
        }

        writer.WriteLine();
    }

    public static string Escape(string value)
    {
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuote)
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
