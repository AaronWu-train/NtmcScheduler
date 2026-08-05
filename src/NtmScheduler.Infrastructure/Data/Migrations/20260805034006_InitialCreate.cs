using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OwnerId = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    HomeStation = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Specialty = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Ability = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficialScheduleVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Month = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialScheduleVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleCycles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    End = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RequiredR = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 16),
                    RequiredR1 = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    TargetMonth = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScheduleStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    OptimizationStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Seed = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgramVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProgressJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    CandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ShortageAnalysisAvailable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeMonthlyShifts",
                columns: table => new
                {
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Month = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Shift = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeMonthlyShifts", x => new { x.EmployeeId, x.Month });
                    table.ForeignKey(
                        name: "FK_EmployeeMonthlyShifts_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FixedEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: true),
                    End = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedEvents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CandidateSolutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    IsShortageAnalysis = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetricsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CoverageCsv = table.Column<string>(type: "TEXT", nullable: true),
                    ViolationsCsv = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateSolutions_ScheduleRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ScheduleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceCandidateId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftSchedules_CandidateSolutions_SourceCandidateId",
                        column: x => x.SourceCandidateId,
                        principalTable: "CandidateSolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftSchedules_ScheduleRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ScheduleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DraftEdits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftId = table.Column<long>(type: "INTEGER", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BeforeState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AfterState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftEdits_DraftSchedules_DraftId",
                        column: x => x.DraftId,
                        principalTable: "DraftSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_OwnerType_OwnerId",
                table: "Assignments",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_OwnerType_OwnerId_EmployeeId_Date",
                table: "Assignments",
                columns: new[] { "OwnerType", "OwnerId", "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TargetType_TargetId",
                table: "AuditLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSolutions_RunId_Index",
                table: "CandidateSolutions",
                columns: new[] { "RunId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftEdits_DraftId_Seq",
                table: "DraftEdits",
                columns: new[] { "DraftId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftSchedules_RunId",
                table: "DraftSchedules",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftSchedules_SourceCandidateId",
                table: "DraftSchedules",
                column: "SourceCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedEvents_EmployeeId_Type_Date",
                table: "FixedEvents",
                columns: new[] { "EmployeeId", "Type", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialScheduleVersions_Unit_Month_IsCurrent",
                table: "OfficialScheduleVersions",
                columns: new[] { "Unit", "Month", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialScheduleVersions_Unit_Month_VersionNo",
                table: "OfficialScheduleVersions",
                columns: new[] { "Unit", "Month", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleSettings_Unit_RuleId",
                table: "RuleSettings",
                columns: new[] { "Unit", "RuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleCycles_Start",
                table: "ScheduleCycles",
                column: "Start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_Unit_TargetMonth_CreatedAt",
                table: "ScheduleRuns",
                columns: new[] { "Unit", "TargetMonth", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DraftEdits");

            migrationBuilder.DropTable(
                name: "EmployeeMonthlyShifts");

            migrationBuilder.DropTable(
                name: "FixedEvents");

            migrationBuilder.DropTable(
                name: "OfficialScheduleVersions");

            migrationBuilder.DropTable(
                name: "RuleSettings");

            migrationBuilder.DropTable(
                name: "ScheduleCycles");

            migrationBuilder.DropTable(
                name: "DraftSchedules");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "CandidateSolutions");

            migrationBuilder.DropTable(
                name: "ScheduleRuns");
        }
    }
}
