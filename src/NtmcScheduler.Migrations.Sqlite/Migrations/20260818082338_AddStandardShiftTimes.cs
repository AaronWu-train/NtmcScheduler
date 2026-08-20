using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStandardShiftTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StandardShiftTimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Workspace = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    Shift = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardShiftTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardShiftTimes_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StandardShiftTimes_ConfigurationRevisionId_Workspace_Shift",
                table: "StandardShiftTimes",
                columns: new[] { "ConfigurationRevisionId", "Workspace", "Shift" },
                unique: true);

            // Back-fill existing revisions with current hard-coded defaults.
            // Derive valid GUIDs from each revision id by replacing the last hex digit.
            // This keeps a proper GUID format and produces six stable ids per revision.
            var seedRows = new[]
            {
                ("M", "Early",     "06:30:00", "14:30:00", "1"),
                ("M", "Afternoon", "14:20:00", "22:20:00", "2"),
                ("M", "Night",     "22:00:00", "07:00:00", "3"),
                ("T", "Early",     "07:00:00", "15:00:00", "4"),
                ("T", "Afternoon", "15:00:00", "23:00:00", "5"),
                ("T", "Night",     "23:00:00", "07:00:00", "6"),
            };
            foreach (var (ws, shift, start, end, suffix) in seedRows)
            {
                migrationBuilder.Sql(
                    $"INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime) " +
                    $"SELECT substr(Id, 1, 35) || '{suffix}', Id, '{ws}', '{shift}', '{start}', '{end}' FROM ConfigurationRevisions;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandardShiftTimes");
        }
    }
}
