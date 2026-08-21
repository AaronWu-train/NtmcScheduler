using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlySchedulingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MonthlySettingsJson",
                table: "ScheduleVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleWeightsJson",
                table: "ScheduleVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleWeightsJson",
                table: "ScheduleRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneralRestTarget",
                table: "DemandDrafts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MStationSettingsJson",
                table: "DemandDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecialRestTarget",
                table: "DemandDrafts",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthlySettingsJson",
                table: "ScheduleVersions");

            migrationBuilder.DropColumn(
                name: "RuleWeightsJson",
                table: "ScheduleVersions");

            migrationBuilder.DropColumn(
                name: "RuleWeightsJson",
                table: "ScheduleRuns");

            migrationBuilder.DropColumn(
                name: "GeneralRestTarget",
                table: "DemandDrafts");

            migrationBuilder.DropColumn(
                name: "MStationSettingsJson",
                table: "DemandDrafts");

            migrationBuilder.DropColumn(
                name: "SpecialRestTarget",
                table: "DemandDrafts");
        }
    }
}
