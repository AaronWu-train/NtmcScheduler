using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixStandardShiftTimeGuids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM StandardShiftTimes
                WHERE length(Id) != 36
                   OR Id NOT GLOB '????????-????-????-????-????????????';

                INSERT INTO StandardShiftTimes (Id, ConfigurationRevisionId, Workspace, Shift, StartTime, EndTime)
                SELECT substr(Id, 1, 35) || '4', Id, 'T', 'Early', '07:00:00', '15:00:00'
                FROM ConfigurationRevisions revision
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM StandardShiftTimes shift
                    WHERE shift.ConfigurationRevisionId = revision.Id
                      AND shift.Workspace = 'T'
                      AND shift.Shift = 'Early');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
