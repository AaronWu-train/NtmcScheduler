using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYmWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Workspace",
                table: "MPerpetualScheduleTemplates",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "M");

            migrationBuilder.CreateIndex(
                name: "IX_MPerpetualScheduleTemplates_Workspace",
                table: "MPerpetualScheduleTemplates",
                column: "Workspace",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MPerpetualScheduleTemplates_Workspace",
                table: "MPerpetualScheduleTemplates");

            migrationBuilder.DropColumn(
                name: "Workspace",
                table: "MPerpetualScheduleTemplates");
        }
    }
}
