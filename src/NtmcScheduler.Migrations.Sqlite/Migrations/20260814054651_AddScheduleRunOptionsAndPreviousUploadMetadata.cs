using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRunOptionsAndPreviousUploadMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sqlServer = ActiveProvider?.Contains("SqlServer", StringComparison.Ordinal) == true;
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "UploadedPreviousSchedules",
                type: sqlServer ? "nvarchar(max)" : "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SeedCount",
                table: "ScheduleRuns",
                type: sqlServer ? "int" : "INTEGER",
                nullable: false,
                defaultValue: 1);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileName",
                table: "UploadedPreviousSchedules");

            migrationBuilder.DropColumn(
                name: "SeedCount",
                table: "ScheduleRuns");
        }
    }
}
