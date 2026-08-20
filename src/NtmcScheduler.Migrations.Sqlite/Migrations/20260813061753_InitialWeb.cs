using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    MustChangePassword = table.Column<bool>(nullable: false),
                    IsDisabled = table.Column<bool>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    UserName = table.Column<string>(maxLength: 100, nullable: true),
                    NormalizedUserName = table.Column<string>(maxLength: 100, nullable: true),
                    Email = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(nullable: false),
                    PasswordHash = table.Column<string>(nullable: true),
                    SecurityStamp = table.Column<string>(nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true),
                    PhoneNumber = table.Column<string>(nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(nullable: false),
                    TwoFactorEnabled = table.Column<bool>(nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(nullable: true),
                    LockoutEnabled = table.Column<bool>(nullable: false),
                    AccessFailedCount = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(nullable: false),
                    AtUtcTicks = table.Column<long>(nullable: false),
                    ActorUserId = table.Column<Guid>(nullable: true),
                    ActorName = table.Column<string>(maxLength: 100, nullable: false),
                    Action = table.Column<string>(maxLength: 80, nullable: false),
                    Workspace = table.Column<int>(nullable: true),
                    ResourceType = table.Column<string>(maxLength: 80, nullable: false),
                    ResourceId = table.Column<string>(maxLength: 80, nullable: false),
                    Succeeded = table.Column<bool>(nullable: false),
                    BeforeJson = table.Column<string>(nullable: true),
                    AfterJson = table.Column<string>(nullable: true),
                    IpAddress = table.Column<string>(nullable: true),
                    UserAgent = table.Column<string>(nullable: true),
                    CorrelationId = table.Column<string>(maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Version = table.Column<int>(nullable: false),
                    CreatedByUserId = table.Column<Guid>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    EmployeeCode = table.Column<string>(maxLength: 32, nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(nullable: true),
                    Ability = table.Column<int>(nullable: true),
                    IsArchived = table.Column<bool>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    RevisionToken = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(nullable: false),
                    Status = table.Column<string>(maxLength: 32, nullable: false),
                    DemandDraftId = table.Column<Guid>(nullable: false),
                    RequestedByUserId = table.Column<Guid>(nullable: false),
                    RequestedByName = table.Column<string>(maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(nullable: true),
                    UserAgent = table.Column<string>(nullable: true),
                    RandomSeed = table.Column<int>(nullable: false),
                    WorkerCount = table.Column<int>(nullable: false),
                    TimeLimitSeconds = table.Column<int>(nullable: false),
                    ProgramVersion = table.Column<string>(maxLength: 100, nullable: false),
                    InputHash = table.Column<string>(maxLength: 128, nullable: false),
                    InputSnapshotJson = table.Column<string>(nullable: false),
                    PerpetualScheduleJson = table.Column<string>(nullable: true),
                    Error = table.Column<string>(nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadedPreviousSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(nullable: false),
                    ParsedScheduleJson = table.Column<string>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedPreviousSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(nullable: false),
                    ProviderKey = table.Column<string>(nullable: false),
                    ProviderDisplayName = table.Column<string>(nullable: true),
                    UserId = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(nullable: false),
                    RoleId = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(nullable: false),
                    LoginProvider = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspacePermissions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePermissions", x => new { x.UserId, x.Workspace });
                    table.ForeignKey(
                        name: "FK_WorkspacePermissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurrentConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigurationRevisionId = table.Column<Guid>(nullable: false),
                    RevisionToken = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurrentConfigurations_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NonStandardShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(maxLength: 50, nullable: true),
                    Code = table.Column<string>(maxLength: 32, nullable: false),
                    StartTime = table.Column<TimeOnly>(nullable: false),
                    EndTime = table.Column<TimeOnly>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonStandardShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NonStandardShifts_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(nullable: false),
                    Start = table.Column<DateOnly>(nullable: false),
                    End = table.Column<DateOnly>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestIntervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestIntervals_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    SourceRunId = table.Column<Guid>(nullable: true),
                    CandidateIndex = table.Column<int>(nullable: true),
                    SourceStatus = table.Column<string>(maxLength: 32, nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(nullable: false),
                    HasErrors = table.Column<bool>(nullable: false),
                    WarningCount = table.Column<int>(nullable: false),
                    IsArchived = table.Column<bool>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    CreatedByUserId = table.Column<Guid>(nullable: false),
                    UpdatedByUserId = table.Column<Guid>(nullable: false),
                    RevisionToken = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleVersions_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleVersions_ScheduleRuns_SourceRunId",
                        column: x => x.SourceRunId,
                        principalTable: "ScheduleRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DemandDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(nullable: false),
                    PreviousSource = table.Column<string>(maxLength: 32, nullable: false),
                    PreviousAdoptedScheduleVersionId = table.Column<Guid>(nullable: true),
                    UploadedPreviousScheduleId = table.Column<Guid>(nullable: true),
                    ConfigurationRevisionId = table.Column<Guid>(nullable: false),
                    PerpetualScheduleJson = table.Column<string>(nullable: true),
                    CreatedByUserId = table.Column<Guid>(nullable: false),
                    UpdatedByUserId = table.Column<Guid>(nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                    RevisionToken = table.Column<Guid>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandDrafts_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DemandDrafts_UploadedPreviousSchedules_UploadedPreviousScheduleId",
                        column: x => x.UploadedPreviousScheduleId,
                        principalTable: "UploadedPreviousSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NationalHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    RestIntervalId = table.Column<Guid>(nullable: false),
                    Date = table.Column<DateOnly>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NationalHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NationalHolidays_RestIntervals_RestIntervalId",
                        column: x => x.RestIntervalId,
                        principalTable: "RestIntervals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdoptedSchedules",
                columns: table => new
                {
                    Workspace = table.Column<string>(maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(nullable: false),
                    ScheduleVersionId = table.Column<Guid>(nullable: false),
                    AdoptedByUserId = table.Column<Guid>(nullable: false),
                    AdoptedAtUtc = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdoptedSchedules", x => new { x.Workspace, x.Month });
                    table.ForeignKey(
                        name: "FK_AdoptedSchedules_ScheduleVersions_ScheduleVersionId",
                        column: x => x.ScheduleVersionId,
                        principalTable: "ScheduleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ScheduleVersionId = table.Column<Guid>(nullable: false),
                    Date = table.Column<DateOnly>(nullable: false),
                    Station = table.Column<string>(nullable: false),
                    Shift = table.Column<string>(nullable: false),
                    Count = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalAssignments_ScheduleVersions_ScheduleVersionId",
                        column: x => x.ScheduleVersionId,
                        principalTable: "ScheduleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleEmployeeSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ScheduleVersionId = table.Column<Guid>(nullable: false),
                    EmployeeCode = table.Column<string>(maxLength: 32, nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Affiliation = table.Column<string>(nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(nullable: true),
                    Ability = table.Column<int>(nullable: true),
                    MonthlyShift = table.Column<string>(nullable: true),
                    OpeningRest = table.Column<int>(nullable: true),
                    OpeningSpecialRest = table.Column<int>(nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(nullable: false),
                    ClosingRest = table.Column<int>(nullable: true),
                    ClosingSpecialRest = table.Column<int>(nullable: true),
                    NormalWorkCount = table.Column<int>(nullable: true),
                    PerpetualScheduleId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleEmployeeSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEmployeeSnapshots_ScheduleVersions_ScheduleVersionId",
                        column: x => x.ScheduleVersionId,
                        principalTable: "ScheduleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemandEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    DemandDraftId = table.Column<Guid>(nullable: false),
                    EmployeeCode = table.Column<string>(maxLength: 32, nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(nullable: true),
                    Ability = table.Column<int>(nullable: true),
                    MonthlyShift = table.Column<string>(nullable: true),
                    OpeningRest = table.Column<int>(nullable: true),
                    OpeningSpecialRest = table.Column<int>(nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(nullable: false),
                    PerpetualScheduleId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandEmployees_DemandDrafts_DemandDraftId",
                        column: x => x.DemandDraftId,
                        principalTable: "DemandDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    ScheduleEmployeeSnapshotId = table.Column<Guid>(nullable: false),
                    Date = table.Column<DateOnly>(nullable: false),
                    Kind = table.Column<string>(maxLength: 32, nullable: false),
                    RequestedRest = table.Column<bool>(nullable: false),
                    Station = table.Column<string>(nullable: true),
                    Shift = table.Column<string>(nullable: true),
                    EventStart = table.Column<DateTimeOffset>(nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(nullable: true),
                    EventDescription = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleAssignments_ScheduleEmployeeSnapshots_ScheduleEmployeeSnapshotId",
                        column: x => x.ScheduleEmployeeSnapshotId,
                        principalTable: "ScheduleEmployeeSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemandAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    DemandEmployeeId = table.Column<Guid>(nullable: false),
                    Date = table.Column<DateOnly>(nullable: false),
                    Kind = table.Column<string>(nullable: true),
                    RequestedRest = table.Column<bool>(nullable: false),
                    Station = table.Column<string>(nullable: true),
                    Shift = table.Column<string>(nullable: true),
                    EventStart = table.Column<DateTimeOffset>(nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(nullable: true),
                    EventDescription = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandAssignments_DemandEmployees_DemandEmployeeId",
                        column: x => x.DemandEmployeeId,
                        principalTable: "DemandEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdoptedSchedules_ScheduleVersionId",
                table: "AdoptedSchedules",
                column: "ScheduleVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AtUtcTicks",
                table: "AuditLogs",
                column: "AtUtcTicks");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Workspace_Action",
                table: "AuditLogs",
                columns: new[] { "Workspace", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRevisions_Version",
                table: "ConfigurationRevisions",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrentConfigurations_ConfigurationRevisionId",
                table: "CurrentConfigurations",
                column: "ConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandAssignments_DemandEmployeeId_Date",
                table: "DemandAssignments",
                columns: new[] { "DemandEmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandDrafts_ConfigurationRevisionId",
                table: "DemandDrafts",
                column: "ConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandDrafts_UploadedPreviousScheduleId",
                table: "DemandDrafts",
                column: "UploadedPreviousScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandDrafts_Workspace_Month",
                table: "DemandDrafts",
                columns: new[] { "Workspace", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandEmployees_DemandDraftId_EmployeeCode",
                table: "DemandEmployees",
                columns: new[] { "DemandDraftId", "EmployeeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Workspace_EmployeeCode",
                table: "Employees",
                columns: new[] { "Workspace", "EmployeeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAssignments_ScheduleVersionId",
                table: "ExternalAssignments",
                column: "ScheduleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_NationalHolidays_RestIntervalId_Date",
                table: "NationalHolidays",
                columns: new[] { "RestIntervalId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonStandardShifts_ConfigurationRevisionId_Code",
                table: "NonStandardShifts",
                columns: new[] { "ConfigurationRevisionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestIntervals_ConfigurationRevisionId_Start",
                table: "RestIntervals",
                columns: new[] { "ConfigurationRevisionId", "Start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_ScheduleEmployeeSnapshotId_Date",
                table: "ScheduleAssignments",
                columns: new[] { "ScheduleEmployeeSnapshotId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEmployeeSnapshots_ScheduleVersionId_EmployeeCode",
                table: "ScheduleEmployeeSnapshots",
                columns: new[] { "ScheduleVersionId", "EmployeeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_Workspace_Month_CreatedAtUtc",
                table: "ScheduleRuns",
                columns: new[] { "Workspace", "Month", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleVersions_ConfigurationRevisionId",
                table: "ScheduleVersions",
                column: "ConfigurationRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleVersions_SourceRunId",
                table: "ScheduleVersions",
                column: "SourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleVersions_Workspace_Month_CreatedAtUtc",
                table: "ScheduleVersions",
                columns: new[] { "Workspace", "Month", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdoptedSchedules");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CurrentConfigurations");

            migrationBuilder.DropTable(
                name: "DemandAssignments");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "ExternalAssignments");

            migrationBuilder.DropTable(
                name: "NationalHolidays");

            migrationBuilder.DropTable(
                name: "NonStandardShifts");

            migrationBuilder.DropTable(
                name: "ScheduleAssignments");

            migrationBuilder.DropTable(
                name: "WorkspacePermissions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "DemandEmployees");

            migrationBuilder.DropTable(
                name: "RestIntervals");

            migrationBuilder.DropTable(
                name: "ScheduleEmployeeSnapshots");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "DemandDrafts");

            migrationBuilder.DropTable(
                name: "ScheduleVersions");

            migrationBuilder.DropTable(
                name: "UploadedPreviousSchedules");

            migrationBuilder.DropTable(
                name: "ConfigurationRevisions");

            migrationBuilder.DropTable(
                name: "ScheduleRuns");
        }
    }
}
