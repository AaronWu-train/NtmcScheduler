using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDemandSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemandSubmissionImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DemandDraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportedByName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandSubmissionImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandSubmissionImports_DemandDrafts_DemandDraftId",
                        column: x => x.DemandDraftId,
                        principalTable: "DemandDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDemandSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Workspace = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EmployeeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedByName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDemandSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDemandSubmissionAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedRest = table.Column<bool>(type: "INTEGER", nullable: false),
                    Station = table.Column<string>(type: "TEXT", nullable: true),
                    Shift = table.Column<string>(type: "TEXT", nullable: true),
                    EventStart = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EventDescription = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDemandSubmissionAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDemandSubmissionAssignments_EmployeeDemandSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "EmployeeDemandSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemandSubmissionImports_DemandDraftId",
                table: "DemandSubmissionImports",
                column: "DemandDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDemandSubmissionAssignments_SubmissionId_Date",
                table: "EmployeeDemandSubmissionAssignments",
                columns: new[] { "SubmissionId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDemandSubmissions_Workspace_Month_EmployeeCode",
                table: "EmployeeDemandSubmissions",
                columns: new[] { "Workspace", "Month", "EmployeeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemandSubmissionImports");

            migrationBuilder.DropTable(
                name: "EmployeeDemandSubmissionAssignments");

            migrationBuilder.DropTable(
                name: "EmployeeDemandSubmissions");
        }
    }
}
