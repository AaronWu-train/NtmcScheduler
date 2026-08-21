using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class MakeCurrentConfigurationKeyExplicit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE [CurrentConfigurations_Temp] (
                    [Id] int NOT NULL,
                    [ConfigurationRevisionId] uniqueidentifier NOT NULL,
                    [RevisionToken] uniqueidentifier NOT NULL
                );

                INSERT INTO [CurrentConfigurations_Temp] ([Id], [ConfigurationRevisionId], [RevisionToken])
                SELECT [Id], [ConfigurationRevisionId], [RevisionToken]
                FROM [CurrentConfigurations];

                DROP TABLE [CurrentConfigurations];
                EXEC sp_rename N'[CurrentConfigurations_Temp]', N'CurrentConfigurations';

                ALTER TABLE [CurrentConfigurations]
                    ADD CONSTRAINT [PK_CurrentConfigurations] PRIMARY KEY ([Id]);
                CREATE INDEX [IX_CurrentConfigurations_ConfigurationRevisionId]
                    ON [CurrentConfigurations] ([ConfigurationRevisionId]);
                ALTER TABLE [CurrentConfigurations]
                    ADD CONSTRAINT [FK_CurrentConfigurations_ConfigurationRevisions_ConfigurationRevisionId]
                    FOREIGN KEY ([ConfigurationRevisionId]) REFERENCES [ConfigurationRevisions] ([Id])
                    ON DELETE NO ACTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE [CurrentConfigurations_Temp] (
                    [Id] int IDENTITY(1, 1) NOT NULL,
                    [ConfigurationRevisionId] uniqueidentifier NOT NULL,
                    [RevisionToken] uniqueidentifier NOT NULL
                );

                SET IDENTITY_INSERT [CurrentConfigurations_Temp] ON;
                INSERT INTO [CurrentConfigurations_Temp] ([Id], [ConfigurationRevisionId], [RevisionToken])
                SELECT [Id], [ConfigurationRevisionId], [RevisionToken]
                FROM [CurrentConfigurations];
                SET IDENTITY_INSERT [CurrentConfigurations_Temp] OFF;

                DROP TABLE [CurrentConfigurations];
                EXEC sp_rename N'[CurrentConfigurations_Temp]', N'CurrentConfigurations';

                ALTER TABLE [CurrentConfigurations]
                    ADD CONSTRAINT [PK_CurrentConfigurations] PRIMARY KEY ([Id]);
                CREATE INDEX [IX_CurrentConfigurations_ConfigurationRevisionId]
                    ON [CurrentConfigurations] ([ConfigurationRevisionId]);
                ALTER TABLE [CurrentConfigurations]
                    ADD CONSTRAINT [FK_CurrentConfigurations_ConfigurationRevisions_ConfigurationRevisionId]
                    FOREIGN KEY ([ConfigurationRevisionId]) REFERENCES [ConfigurationRevisions] ([Id])
                    ON DELETE NO ACTION;
                """);
        }
    }
}
