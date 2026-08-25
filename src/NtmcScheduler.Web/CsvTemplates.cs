using System.Text;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Web;

public static class CsvTemplates
{
    private static readonly DateOnly SampleMonth = new(2026, 9, 1);

    public static byte[] Users() => Text(
        "帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T",
        "example.user,temp1234,0,1,0,0,0");

    public static byte[]? Create(WorkspaceCode workspace, string kind) => kind.ToLowerInvariant() switch
    {
        "employees" => Employees(workspace),
        "demand" => ScheduleCsv.WriteMonthlyDownload(Monthly(workspace, historical: false), workspace),
        "previous" => ScheduleCsv.WriteMonthlyDownload(Monthly(workspace, historical: true), workspace),
        "perpetual" when workspace.IsStation() => WithBom(ScheduleCsv.WriteMPerpetualSchedule(Perpetual(workspace), workspace)),
        "rest-intervals" => Text(
            "區間開始日期,區間結束日期,國定假日日期",
            "2026-07-20,2026-09-13,2026-08-14"),
        "non-standard-shifts" => Text("班型,時間,代碼", "早一,06:30~14:30,0635"),
        _ => null
    };

    private static byte[] Employees(WorkspaceCode workspace) => workspace switch
    {
        WorkspaceCode.M => Text("ID,姓名,所屬車站,月中開始排班日", "1000M0001,王小明,LB01,"),
        WorkspaceCode.YM => Text("ID,姓名,所屬車站,月中開始排班日", "1000Y0001,王小明,Y06,"),
        WorkspaceCode.T => Text("ID,姓名,所屬,月中開始排班日,能力", "1209M0001,王小明,車輛軌道組,,5"),
        WorkspaceCode.YT => Text("ID,姓名,所屬,月中開始排班日,能力", "1209Y0001,王小明,車輛軌道組,,5"),
        _ => throw new ArgumentOutOfRangeException(nameof(workspace))
    };

    private static MonthlySchedule Monthly(WorkspaceCode workspace, bool historical)
    {
        var employee = Employee(workspace);
        if (!historical)
        {
            employee = employee with
            {
                OpeningUsage = new(10, 0),
                RequestedLeaveRestCount = 2,
                Assignments = new Dictionary<DateOnly, ScheduleCell>
                {
                    [SampleMonth.AddDays(7)] = new() { RequestedRest = true }
                }
            };
            return new(SampleMonth, [employee]);
        }

        var assignments = Enumerable.Range(0, 30).ToDictionary(
            day => SampleMonth.AddDays(day),
            day => day % 7 == 0
                ? new ScheduleCell { Kind = AssignmentKind.Rest, RequestedRest = day == 14 }
                : day == 1
                    ? new ScheduleCell { Kind = AssignmentKind.LeaveRest }
                    : Work(workspace, day));
        employee = employee with
        {
            OpeningUsage = new(1, 0),
            Assignments = assignments,
            ClosingUsage = new(11, 0),
            NormalWorkCount = assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work)
        };
        return new(SampleMonth, [employee]);
    }

    private static EmployeeMonthlySchedule Employee(WorkspaceCode workspace) => new()
    {
        EmployeeId = workspace switch
        {
            WorkspaceCode.M => "1000M0001",
            WorkspaceCode.YM => "1000Y0001",
            WorkspaceCode.T => "1209M0001",
            WorkspaceCode.YT => "1209Y0001",
            _ => throw new ArgumentOutOfRangeException(nameof(workspace))
        },
        Name = "王小明",
        Affiliation = workspace.IsStation() ? workspace.Stations()[0] : "車輛軌道組",
        Ability = workspace.IsMaintenance() ? 5 : null,
        MonthlyShift = workspace.IsMaintenance() ? Shift.Early : null,
        PerpetualScheduleId = workspace.IsStation() ? $"{workspace.Stations()[0]}-1" : null,
        Assignments = new Dictionary<DateOnly, ScheduleCell>()
    };

    private static ScheduleCell Work(WorkspaceCode workspace, int day) => new()
    {
        Kind = AssignmentKind.Work,
        Station = workspace.IsStation() ? workspace.Stations()[0] : null,
        Shift = workspace.IsStation() ? (Shift)(day % 3) : Shift.Early
    };

    private static MPerpetualSchedule Perpetual(WorkspaceCode workspace) => new(
        new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            [$"{workspace.Stations()[0]}-1"] = Enumerable.Range(0, 56)
                .Select(day => day % 7 is 0 or 4
                    ? new ScheduleCell { Kind = AssignmentKind.Rest }
                    : Work(workspace, day))
                .ToArray()
        });

    private static byte[] Text(string header, string row) =>
        Encoding.UTF8.GetBytes($"\uFEFF{header}{Environment.NewLine}{row}{Environment.NewLine}");

    private static byte[] WithBom(byte[] content) => content.AsSpan().StartsWith(Encoding.UTF8.Preamble)
        ? content
        : [.. Encoding.UTF8.Preamble, .. content];
}
