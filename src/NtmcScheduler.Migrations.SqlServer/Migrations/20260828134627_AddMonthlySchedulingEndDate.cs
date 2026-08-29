using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlySchedulingEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEndDate",
                table: "ScheduleEmployeeSnapshots",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEndDate",
                table: "DemandEmployees",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmploymentEndDate",
                table: "ScheduleEmployeeSnapshots");

            migrationBuilder.DropColumn(
                name: "EmploymentEndDate",
                table: "DemandEmployees");
        }
    }
}
