using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRunResultDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var textType = ActiveProvider?.Contains("SqlServer", StringComparison.Ordinal) == true ? "nvarchar(max)" : "TEXT";
            migrationBuilder.AddColumn<string>(
                name: "ResultDetailsJson",
                table: "ScheduleRuns",
                type: textType,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultDetailsJson",
                table: "ScheduleRuns");
        }
    }
}
