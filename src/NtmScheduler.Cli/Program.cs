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
            var nonStandardShiftsPath = Default(Ask("非常態班型 CSV [non-standard-shifts.csv]："), "non-standard-shifts.csv");
            var nonStandardShifts = ScheduleCsv.ReadNonStandardShifts(nonStandardShiftsPath);

            var previous = string.IsNullOrWhiteSpace(previousPath)
                ? new MonthlySchedule(month.AddMonths(-1), [])
                : ScheduleCsv.ReadMonthly(previousPath, month.AddMonths(-1), nonStandardShifts, historical: true);
            var demand = ScheduleCsv.ReadMonthly(demandPath, month, nonStandardShifts);
            var input = new ScheduleInput(previous, demand, ScheduleCsv.ReadRestIntervals(intervalsPath), nonStandardShifts);
            var isT = DetectT(demand);

            if (isT) return Finish(TSolver.Solve(input, cancellationToken: cancellation.Token));
            var perpetualSchedulePath = Ask("M 八週萬年班表 CSV（留空不使用 hint）：");
            return string.IsNullOrWhiteSpace(perpetualSchedulePath)
                ? Finish(MSolver.Solve(input, cancellationToken: cancellation.Token))
                : Finish(MSolver.Solve(input, ScheduleCsv.ReadMPerpetualSchedule(perpetualSchedulePath), cancellationToken: cancellation.Token));
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

    private static int Finish(TSolveResult result)
    {
        Print(result.Status, result.Errors, result.Candidates.Select(candidate => candidate.Objectives).ToArray());
        if (result.Candidates.Count == 0) return 1;
        var firstNumber = NextAvailableCandidateNumber(result.Candidates.Count);
        for (var index = 0; index < result.Candidates.Count; index++)
            ScheduleCsv.WriteMonthly($"candidate-{firstNumber + index}.csv", result.Candidates[index].Schedule);
        Console.WriteLine($"已輸出 {result.Candidates.Count} 份候選班表。");
        return 0;
    }

    private static void Print(SolveStatus status, IReadOnlyList<InputError> errors, IReadOnlyList<IReadOnlyList<ObjectiveScore>> candidates)
    {
        Console.WriteLine($"狀態：{StatusText(status)}");
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
        "ScheduleQuality" => "綜合排班品質",
        "Fairness" => "站務配置與公平性",
        "StaffingQuality" => "班組人力品質",
        "RestDistribution" => "休假分布",
        "WorkPatternQuality" => "工作型態品質",
        "RestFairness" => "休假公平",
        _ => name
    };

    private static string ComponentText(string name) => name switch
    {
        "RequestedRest" => "未滿足指定休假",
        "UnusedLeaveRest" => "未使用指定 R休額度",
        "ExternalStaffing" => "外援人力",
        "MonthlyRest" => "每月一般 R 偏差",
        "SpecialRestBalance" => "八週累積 R1 餘額",
        "NonHomeStation" => "非所屬站指派",
        "WorkStreak" => "連續工作區段",
        "MixedShiftWorkStreak" => "工作區段混合班型",
        "NightRestEarly" => "夜休早",
        "NightRestAfternoon" => "夜休午",
        "ShiftChangeWithoutRest" => "未休假直接換班",
        "NonPreferredRotation" => "非偏好輪轉",
        "WeekdayRestFairness" => "平日休假公平",
        "HolidayRestFairness" => "假日休假公平",
        "SupportFairness" => "跨站支援公平",
        "EarlyShiftFairness" => "早班次數公平",
        "AfternoonShiftFairness" => "午班次數公平",
        "NightShiftFairness" => "夜班次數公平",
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
        "ExternalStaffing" => "原三站外援超過 70 的部分，加上 LB09 全部外援人次",
        "MonthlyRest" => "各人實際 R 數與當月週末日目標差額平方的合計",
        "SpecialRestBalance" => "各人八週區間截至月底的累積 R1 超額，或超過一日容許量的欠額平方合計",
        "NonHomeStation" => "員工不在所屬站工作的總日數",
        "WorkStreak" => "各已結束連續工作區段依長度罰分表計算後的合計",
        "MixedShiftWorkStreak" => "包含兩種以上正常班型的已結束工作區段數",
        "NightRestEarly" => "夜班、休假、早班三日組合的次數",
        "NightRestAfternoon" => "夜班、休假、午班三日組合的次數",
        "ShiftChangeWithoutRest" => "兩個正常班之間沒有休假且班別不同的次數",
        "NonPreferredRotation" => "班別改變不符合早、午、夜循環方向的次數",
        "WeekdayRestFairness" => "各比較群組內平日休假最多與最少者差額的合計",
        "HolidayRestFairness" => "各比較群組內假日休假最多與最少者差額的合計",
        "SupportFairness" => "各三站群組內跨站支援最多與最少者差額的合計",
        "EarlyShiftFairness" => "各三站群組的人數乘以各人早班數平方和，再減早班數總和平方的合計",
        "AfternoonShiftFairness" => "各三站群組的人數乘以各人午班數平方和，再減午班數總和平方的合計",
        "NightShiftFairness" => "各三站群組的人數乘以各人夜班數平方和，再減夜班數總和平方的合計",
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
