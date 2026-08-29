using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEndDate",
                table: "DemandEmployees",
                type: "TEXT",
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
