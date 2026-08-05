using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MonthScheduleWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop superseded draft/publish rows (early project; no data migration).
            migrationBuilder.Sql(
                "DELETE FROM Assignments WHERE OwnerType IN ('Draft', 'PublishedVersion');");

            migrationBuilder.DropTable(
                name: "DraftEdits");

            migrationBuilder.DropTable(
                name: "OfficialScheduleVersions");

            migrationBuilder.DropTable(
                name: "DraftSchedules");

            migrationBuilder.CreateTable(
                name: "MonthSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Month = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    SourceRunId = table.Column<long>(type: "INTEGER", nullable: true),
                    SourceCandidateId = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthSchedules_CandidateSolutions_SourceCandidateId",
                        column: x => x.SourceCandidateId,
                        principalTable: "CandidateSolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MonthSchedules_ScheduleRuns_SourceRunId",
                        column: x => x.SourceRunId,
                        principalTable: "ScheduleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Month = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleEdits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduleId = table.Column<long>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_ScheduleEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEdits_MonthSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "MonthSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthSchedules_SourceCandidateId",
                table: "MonthSchedules",
                column: "SourceCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthSchedules_SourceRunId",
                table: "MonthSchedules",
                column: "SourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthSchedules_Unit_Month",
                table: "MonthSchedules",
                columns: new[] { "Unit", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEdits_ScheduleId_Seq",
                table: "ScheduleEdits",
                columns: new[] { "ScheduleId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSnapshots_Unit_Month_IsCurrent",
                table: "ScheduleSnapshots",
                columns: new[] { "Unit", "Month", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSnapshots_Unit_Month_VersionNo",
                table: "ScheduleSnapshots",
                columns: new[] { "Unit", "Month", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleEdits");

            migrationBuilder.DropTable(
                name: "ScheduleSnapshots");

            migrationBuilder.DropTable(
                name: "MonthSchedules");

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
                name: "OfficialScheduleVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Month = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    VersionNo = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialScheduleVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DraftEdits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftId = table.Column<long>(type: "INTEGER", nullable: false),
                    AfterState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BeforeState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "IX_OfficialScheduleVersions_Unit_Month_IsCurrent",
                table: "OfficialScheduleVersions",
                columns: new[] { "Unit", "Month", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialScheduleVersions_Unit_Month_VersionNo",
                table: "OfficialScheduleVersions",
                columns: new[] { "Unit", "Month", "VersionNo" },
                unique: true);
        }
    }
}
