using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerpetualUploadMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider?.Contains("SqlServer", StringComparison.Ordinal) == true;
            migrationBuilder.AddColumn<string>(
                name: "PerpetualScheduleFileName",
                table: "DemandDrafts",
                type: sqlServer ? "nvarchar(max)" : "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PerpetualScheduleUploadedAtUtc",
                table: "DemandDrafts",
                type: sqlServer ? "datetimeoffset" : "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerpetualScheduleFileName",
                table: "DemandDrafts");

            migrationBuilder.DropColumn(
                name: "PerpetualScheduleUploadedAtUtc",
                table: "DemandDrafts");
        }
    }
}
