using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRestBoundsAndRequestedRestLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestedLeaveRestMinimum",
                table: "ScheduleEmployeeSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestedLeaveRestMinimum",
                table: "EmployeeDemandSubmissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestedLeaveRestMinimum",
                table: "DemandEmployees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestedRestLimit",
                table: "DemandDrafts",
                type: "int",
                nullable: false,
                defaultValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedLeaveRestMinimum",
                table: "ScheduleEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "RequestedLeaveRestMinimum",
                table: "EmployeeDemandSubmissions");

            migrationBuilder.DropColumn(
                name: "RequestedLeaveRestMinimum",
                table: "DemandEmployees");

            migrationBuilder.DropColumn(
                name: "RequestedRestLimit",
                table: "DemandDrafts");
        }
    }
}
