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
            migrationBuilder.Sql("""
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'M', 'Early',     '06:30:00', '14:30:00' FROM ConfigurationRevisions;
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'M', 'Afternoon', '14:20:00', '22:20:00' FROM ConfigurationRevisions;
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'M', 'Night',     '22:00:00', '07:00:00' FROM ConfigurationRevisions;
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)) || '-' || hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'T', 'Early',     '07:00:00', '15:00:00' FROM ConfigurationRevisions;
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'T', 'Afternoon', '15:00:00', '23:00:00' FROM ConfigurationRevisions;
                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || '4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                       Id, 'T', 'Night',     '23:00:00', '07:00:00' FROM ConfigurationRevisions;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandardShiftTimes");
        }
    }
}
