using System.Diagnostics;
using System.Globalization;
using System.Text;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Cli;

public static class Program
{
    public static int Main(string[] args)
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
            var previousPath = Default(Ask("上月班表 CSV [previous.csv]："), "previous.csv");
            var demandPath = Default(Ask("本月需求 CSV [demand.csv]："), "demand.csv");
            var intervalsPath = Default(Ask("八週區間 CSV [rest-intervals.csv]："), "rest-intervals.csv");
            var nonStandardShiftsPath = Default(Ask("非常態班型 CSV [non-standard-shifts.csv]："), "non-standard-shifts.csv");
            var nonStandardShifts = ScheduleCsv.ReadNonStandardShifts(nonStandardShiftsPath);

            var previous = string.IsNullOrWhiteSpace(previousPath)
                ? new MonthlySchedule(month.AddMonths(-1), [])
                : ScheduleCsv.ReadMonthly(previousPath, month.AddMonths(-1), nonStandardShifts, historical: true);
            var demand = ScheduleCsv.ReadMonthly(demandPath, month, nonStandardShifts);
            var input = new ScheduleInput(previous, demand, ScheduleCsv.ReadRestIntervals(intervalsPath), nonStandardShifts);
            var isT = DetectT(demand);

            if (isT)
            {
                var search = ReadTSearchOptions(args);
                var solverOptions = new SolverOptions { TimeLimit = TimeSpan.FromSeconds(search.Seconds), WorkerCount = search.Workers };
                Console.WriteLine($"T 求解：總時限 {search.Seconds} 秒、{search.Workers} workers。");
                var startedAt = Stopwatch.GetTimestamp();
                var result = TSolver.Solve(input, solverOptions, cancellation.Token);
                return Finish(result, Stopwatch.GetElapsedTime(startedAt));
            }
            var mSearch = ReadMSearchOptions(args);
            var perpetualSchedulePath = Ask("M 八週萬年班表 CSV（留空不使用 hint）：");
            var perpetualSchedule = string.IsNullOrWhiteSpace(perpetualSchedulePath)
                ? null
                : ScheduleCsv.ReadMPerpetualSchedule(perpetualSchedulePath);
            Console.WriteLine($"M 多 seed 求解：seed 0–{mSearch.Seeds - 1} 依序執行，各 {mSearch.Seconds} 秒、{mSearch.Workers} workers（總時限約 {(long)mSearch.Seeds * mSearch.Seconds} 秒）。");
            var mStartedAt = Stopwatch.GetTimestamp();
            var (mResult, selectedSeed) = SolveMPortfolio(input, perpetualSchedule, mSearch, cancellation.Token);
            Console.WriteLine($"採用 seed：{selectedSeed}");
            return Finish(mResult, Stopwatch.GetElapsedTime(mStartedAt));
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

    private static (MSolveResult Result, int Seed) SolveMPortfolio(
        ScheduleInput input,
        MPerpetualSchedule? perpetualSchedule,
        MSearchOptions search,
        CancellationToken cancellationToken)
    {
        // Seeds run one after another, matching the web worker: each seed gets the full worker count
        // and its own time limit, so the wall time is seeds x seconds.
        (MSolveResult Result, int Seed)? best = null;
        for (var seed = 0; seed < search.Seeds; seed++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new SolverOptions { TimeLimit = TimeSpan.FromSeconds(search.Seconds), RandomSeed = seed, WorkerCount = search.Workers };
            var result = perpetualSchedule is null
                ? MSolver.Solve(input, options, cancellationToken)
                : MSolver.Solve(input, perpetualSchedule, options, cancellationToken);
            if (best is null || CompareMResults(result, best.Value.Result) < 0) best = (result, seed);
        }
        return best ?? throw new InvalidOperationException("seeds must be at least one.");
    }

    private static MSearchOptions ReadMSearchOptions(string[] args)
    {
        if (args.Length == 0) return new(8, 2, 300);
        const string usage = "用法：--search workers=4,seeds=2,seconds=300";
        if (args.Length != 2 || args[0] != "--search") throw new FormatException(usage);
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0] is not ("workers" or "seeds" or "seconds") ||
                !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 ||
                !values.TryAdd(pair[0], value))
                throw new FormatException(usage);
        }
        if (values.Count != 3 || !values.ContainsKey("workers") || !values.ContainsKey("seeds") || !values.ContainsKey("seconds"))
            throw new FormatException(usage);
        return new(values["workers"], values["seeds"], values["seconds"]);
    }

    private static TSearchOptions ReadTSearchOptions(string[] args)
    {
        if (args.Length == 0) return new(8, 300);
        const string usage = "用法：--search workers=8,seconds=300";
        if (args.Length != 2 || args[0] != "--search") throw new FormatException(usage);
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0] is not ("workers" or "seconds") ||
                !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 ||
                !values.TryAdd(pair[0], value))
                throw new FormatException(usage);
        }
        if (values.Count != 2 || !values.ContainsKey("workers") || !values.ContainsKey("seconds"))
            throw new FormatException(usage);
        return new(values["workers"], values["seconds"]);
    }

    private static int CompareMResults(MSolveResult left, MSolveResult right)
    {
        if (left.Candidates.Count == 0 || right.Candidates.Count == 0)
            return right.Candidates.Count.CompareTo(left.Candidates.Count);
        var leftScores = left.Candidates[0].Objectives.OrderBy(score => score.Priority).ToArray();
        var rightScores = right.Candidates[0].Objectives.OrderBy(score => score.Priority).ToArray();
        if (leftScores.Length != rightScores.Length)
            return rightScores.Length.CompareTo(leftScores.Length);
        for (var index = 0; index < leftScores.Length; index++)
        {
            var comparison = leftScores[index].Value.CompareTo(rightScores[index].Value);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private sealed record MSearchOptions(int Workers, int Seeds, int Seconds);
    private sealed record TSearchOptions(int Workers, int Seconds);

    private static int Finish(MSolveResult result, TimeSpan elapsed)
    {
        Print(result.Status, elapsed, result.Errors, result.Candidates.Select(candidate => candidate.Objectives).ToArray());
        if (result.Candidates.Count == 0) return 1;
        var firstNumber = NextAvailableCandidateNumber(result.Candidates.Count);
        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            var number = firstNumber + index;
            ScheduleCsv.WriteMonthly($"candidate-{number}.csv", candidate.Schedule);
            if (candidate.ExternalAssignments.Count > 0) WriteExternal($"candidate-{number}-external.csv", candidate.ExternalAssignments);
        }
        Console.WriteLine($"已輸出 {result.Candidates.Count} 份候選班表。");
        return 0;
    }

    private static int Finish(TSolveResult result, TimeSpan elapsed)
    {
        Print(result.Status, elapsed, result.Errors, result.Candidates.Select(candidate => candidate.Objectives).ToArray());
        if (result.Candidates.Count == 0) return 1;
        var firstNumber = NextAvailableCandidateNumber(result.Candidates.Count);
        for (var index = 0; index < result.Candidates.Count; index++)
            ScheduleCsv.WriteMonthly($"candidate-{firstNumber + index}.csv", result.Candidates[index].Schedule);
        Console.WriteLine($"已輸出 {result.Candidates.Count} 份候選班表。");
        return 0;
    }

    private static void Print(SolveStatus status, TimeSpan elapsed, IReadOnlyList<InputError> errors, IReadOnlyList<IReadOnlyList<ObjectiveScore>> candidates)
    {
        Console.WriteLine($"狀態：{StatusText(status)}");
        Console.WriteLine($"求解耗時：{elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} 秒");
        foreach (var error in errors) Console.Error.WriteLine($"{error.Field}: {error.Message}");
        Console.WriteLine($"候選數量：{candidates.Count}");
        for (var index = 0; index < candidates.Count; index++)
        {
            Console.WriteLine($"候選 {index + 1}：");
            foreach (var objective in candidates[index])
            {
                Console.WriteLine($"  優先層級 {objective.Priority}－{ObjectiveText(objective.Name)}：總加權分 {objective.Value}");
                foreach (var component in objective.Components)
                    Console.WriteLine($"    {ComponentText(component.Name)}：違反量 {component.Value}（{ComponentDescription(component.Name)}），權重 {component.Weight}，加權分 {component.WeightedValue}");
            }
        }
    }

    private static string StatusText(SolveStatus status) => status switch
    {
        SolveStatus.Optimal => "最佳化完成",
        SolveStatus.TimeLimit => "TLE",
        SolveStatus.Infeasible => "硬性規則無解",
        SolveStatus.InvalidInput => "輸入資料無效",
        _ => status.ToString()
    };

    private static string ObjectiveText(string name) => name switch
    {
        "RequestedRest" => "指定休假",
        "ScheduleQualityAndFairness" => "排班品質與公平性",
        "StaffingQuality" => "班組人力品質",
        "RestDistribution" => "休假分布",
        "WorkPatternQuality" => "工作型態品質",
        "RestFairness" => "休假公平",
        _ => name
    };

    private static string ComponentText(string name) => name switch
    {
        "RequestedRest" => "未滿足 R*",
        "UnusedLeaveRest" => "未使用指定 R休額度",
        "ExternalStaffing" => "外援人力",
        "MonthlyRest" => "每月一般 R 偏差",
        "SpecialRestBalance" => "八週累積 R1 餘額",
        "WorkStreak" => "連續工作區段",
        "MixedShiftWorkStreak" => "連續工作區段混合班型",
        "NightRestEarly" => "夜R早",
        "NightRestAfternoon" => "夜R午",
        "ShiftChangeWithoutRest" => "未休假直接換班",
        "WeekdayRestFairness" => "平日休假公平",
        "HolidayRestFairness" => "假日休假公平",
        "EarlyAfternoonImbalance" => "早午班差距",
        "NightShiftTarget" => "夜班 3, 4 天目標",
        "NonMonthlyShift" => "月班別不一致",
        "Attendance" => "班組出勤不足",
        "Specialty" => "專業缺席",
        "Ability" => "高能力人員不足",
        "NightToEarlyRest" => "跨月夜轉早休假不足",
        "MonthBoundaryRestBalance" => "月交界休假不平衡",
        _ => name
    };

    private static string ComponentDescription(string name) => name switch
    {
        "RequestedRest" => "R* 最後未排成 R、R1 或 R休的格數",
        "UnusedLeaveRest" => "各人指定 R休上限減去實際 R休數的合計",
        "ExternalStaffing" => "允許站外援超過 70 的部分，加上盡量不要站的全部外援人次",
        "MonthlyRest" => "各人實際 R 數與當月週末日目標差額平方的合計",
        "SpecialRestBalance" => "各人八週區間截至月底的累積 R1 超額，或超過一日容許量的欠額平方合計",
        "WorkStreak" => "各已結束連續工作區段依長度罰分表計算後的合計",
        "MixedShiftWorkStreak" => "包含兩種以上正常班型的已結束工作區段數",
        "NightRestEarly" => "夜班、休假、早班三日組合的次數",
        "NightRestAfternoon" => "夜班、休假、午班三日組合的次數",
        "ShiftChangeWithoutRest" => "兩個正常班之間沒有休假且班別不同的次數",
        "WeekdayRestFairness" => "各 T 月班別內平日休假次數的最大差合計",
        "HolidayRestFairness" => "各比較群組內假日休假數超出平均正負 1.5 天後的線性罰分合計",
        "EarlyAfternoonImbalance" => "各人當月早班與小班數差超過 4 的部分合計",
        "NightShiftTarget" => "各人當月夜班數依 3 至 4 天目標罰分函數計算後的合計",
        "NonMonthlyShift" => "正常工作班別不同於當月指定班別的格數",
        "Attendance" => "每日各班出勤低於該月班組半數的缺額合計",
        "Specialty" => "每日各班完全無人出勤的應有專業組數",
        "Ability" => "每日各班能力 4 至 5 人員不足兩人的罰分合計；一人計 1、無人計 10",
        "NightToEarlyRest" => "上月最後夜班至本月首次早班不足兩日休假的缺額合計",
        "MonthBoundaryRestBalance" => "夜轉早人員在上月末日與本月首日的休假人數差",
        _ => "此項規則的計算結果"
    };

    private static bool DetectT(MonthlySchedule schedule)
    {
        if (schedule.Employees.Count == 0) throw new ScheduleCsvException("本月需求", "At least one employee is required.");
        var t = schedule.Employees.Count(employee => employee.Ability is not null || employee.MonthlyShift is not null);
        if (t == 0) return false;
        if (t == schedule.Employees.Count && schedule.Employees.All(employee => employee.Ability is not null && employee.MonthlyShift is not null)) return true;
        throw new ScheduleCsvException("能力/T月班別", "M rows must leave both fields blank; T rows must fill both fields.");
    }

    private static int NextAvailableCandidateNumber(int count)
    {
        var first = 1;
        while (Enumerable.Range(first, count).Any(number =>
                   File.Exists($"candidate-{number}.csv") || File.Exists($"candidate-{number}-external.csv")))
            first++;
        return first;
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
    private static string ShiftText(Shift shift) => shift switch { Shift.Early => "早", Shift.Afternoon => "小", Shift.Night => "夜", _ => "" };
}
