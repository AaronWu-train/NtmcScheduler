using Microsoft.EntityFrameworkCore.Migrations;

namespace NtmcScheduler.Infrastructure.Data.Migrations;

public partial class MakeScheduleRunIndependentFromDemand : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var guidType = ActiveProvider?.Contains("SqlServer", StringComparison.Ordinal) == true ? "uniqueidentifier" : "TEXT";
        migrationBuilder.AddColumn<Guid>("ConfigurationRevisionId", "ScheduleRuns", type: guidType, nullable: true);
        migrationBuilder.Sql("""
            UPDATE "ScheduleRuns"
            SET "ConfigurationRevisionId" = (
                SELECT "ConfigurationRevisionId"
                FROM "DemandDrafts"
                WHERE "DemandDrafts"."Id" = "ScheduleRuns"."DemandDraftId"
            )
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ConfigurationRevisionId", "ScheduleRuns");
    }
}
