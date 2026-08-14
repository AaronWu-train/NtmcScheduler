using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeleteEmployeeArchiveFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM Employees WHERE IsArchived = 1");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Employees",
                nullable: false,
                defaultValue: false);
        }
    }
}
