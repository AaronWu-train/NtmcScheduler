using System.Globalization;
using System.Text;
using NtmScheduler.Solvers;

namespace NtmScheduler.Cli;

public static class Program
{
    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var month = ReadMonth();
            var previousPath = Ask("上月班表 CSV（留空代表空歷史）：");
            var demandPath = Default(Ask("本月需求 CSV [demand.csv]："), "demand.csv");
            var intervalsPath = Default(Ask("八週區間 CSV [rest-intervals.csv]："), "rest-intervals.csv");

            var previous = string.IsNullOrWhiteSpace(previousPath)
                ? new MonthlySchedule(month.AddMonths(-1), [])
                : ScheduleCsv.ReadMonthly(previousPath, month.AddMonths(-1));
            var demand = ScheduleCsv.ReadMonthly(demandPath, month);
            var input = new ScheduleInput(previous, demand, ScheduleCsv.ReadRestIntervals(intervalsPath));
            var isT = DetectT(demand);

            return isT
                ? Finish(TSolver.Solve(input, cancellationToken: cancellation.Token))
                : Finish(MSolver.Solve(input, cancellationToken: cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("已取消。");
            return 130;
        }
        catch (ScheduleCsvException exception)
        {
            Console.Error.WriteLine($"{exception.Field}: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static int Finish(MSolveResult result)
    {
        Print(result.Status, result.Errors, result.Candidates.Select(candidate => candidate.Objectives).ToArray());
        if (result.Candidates.Count == 0) return 1;
        var files = result.Candidates.Select((_, index) => $"candidate-{index + 1}.csv")
            .Concat(result.Candidates.Select((candidate, index) => (candidate, index)).Where(value => value.candidate.ExternalAssignments.Count > 0).Select(value => $"candidate-{value.index + 1}-external.csv"))
            .ToArray();
        if (!CanWrite(files))
        {
            Console.WriteLine("已保留原有候選檔。");
            return 0;
        }

        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            ScheduleCsv.WriteMonthly($"candidate-{index + 1}.csv", candidate.Schedule);
            if (candidate.ExternalAssignments.Count > 0) WriteExternal($"candidate-{index + 1}-external.csv", candidate.ExternalAssignments);
        }
        Console.WriteLine($"已輸出 {result.Candidates.Count} 份候選班表。");
        return 0;
    }

    private static int Finish(TSolveResult result)
    {
        Print(result.Status, result.Errors, result.Candidates.Select(candidate => candidate.Objectives).ToArray());
        if (result.Candidates.Count == 0) return 1;
        var files = result.Candidates.Select((_, index) => $"candidate-{index + 1}.csv").ToArray();
        if (!CanWrite(files))
        {
            Console.WriteLine("已保留原有候選檔。");
            return 0;
        }
        for (var index = 0; index < result.Candidates.Count; index++)
            ScheduleCsv.WriteMonthly($"candidate-{index + 1}.csv", result.Candidates[index].Schedule);
        Console.WriteLine($"已輸出 {result.Candidates.Count} 份候選班表。");
        return 0;
    }

    private static void Print(SolveStatus status, IReadOnlyList<InputError> errors, IReadOnlyList<IReadOnlyList<ObjectiveScore>> candidates)
    {
        Console.WriteLine($"狀態：{status}");
        foreach (var error in errors) Console.Error.WriteLine($"{error.Field}: {error.Message}");
        Console.WriteLine($"候選數量：{candidates.Count}");
        for (var index = 0; index < candidates.Count; index++)
        {
            Console.WriteLine($"候選 {index + 1}：");
            foreach (var objective in candidates[index]) Console.WriteLine($"  Priority {objective.Priority} {objective.Name}: {objective.Value}");
        }
    }

    private static bool DetectT(MonthlySchedule schedule)
    {
        if (schedule.Employees.Count == 0) throw new ScheduleCsvException("本月需求", "At least one employee is required.");
        var t = schedule.Employees.Count(employee => employee.Ability is not null || employee.MonthlyShift is not null);
        if (t == 0) return false;
        if (t == schedule.Employees.Count && schedule.Employees.All(employee => employee.Ability is not null && employee.MonthlyShift is not null)) return true;
        throw new ScheduleCsvException("能力/T月班別", "M rows must leave both fields blank; T rows must fill both fields.");
    }

    private static bool CanWrite(IEnumerable<string> paths)
    {
        if (!paths.Any(File.Exists)) return true;
        var answer = Ask("候選輸出檔已存在，是否覆寫？ [y/N]：");
        return answer.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteExternal(string path, IReadOnlyList<MExternalAssignment> assignments)
    {
        var lines = new List<string> { "日期,車站,班別,人數" };
        lines.AddRange(assignments.Select(value => string.Join(',', value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.Station, ShiftText(value.Shift), value.Count)));
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(true));
    }

    private static DateOnly ReadMonth()
    {
        var text = Ask("目標月份（yyyy-MM）：");
        if (!DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            throw new FormatException("目標月份必須使用 yyyy-MM。");
        return month;
    }

    private static string Ask(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? throw new FormatException("輸入已結束。");
    }

    private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string ShiftText(Shift shift) => shift switch { Shift.Early => "早", Shift.Afternoon => "午", Shift.Night => "夜", _ => "" };
}
