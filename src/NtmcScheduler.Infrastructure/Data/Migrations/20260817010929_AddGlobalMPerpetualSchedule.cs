using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalMPerpetualSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider?.Contains("SqlServer", StringComparison.Ordinal) == true;
            migrationBuilder.CreateTable(
                name: "MPerpetualScheduleTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: sqlServer ? "int" : "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: sqlServer ? "nvarchar(255)" : "TEXT", maxLength: 255, nullable: false),
                    ScheduleJson = table.Column<string>(type: sqlServer ? "nvarchar(max)" : "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: sqlServer ? "uniqueidentifier" : "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: sqlServer ? "datetimeoffset" : "TEXT", nullable: false),
                    RevisionToken = table.Column<Guid>(type: sqlServer ? "uniqueidentifier" : "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MPerpetualScheduleTemplates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MPerpetualScheduleTemplates");
        }
    }
}
