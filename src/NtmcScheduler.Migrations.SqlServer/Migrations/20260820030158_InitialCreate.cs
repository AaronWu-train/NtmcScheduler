using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NtmcScheduler.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    IsDisabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Workspace = table.Column<int>(type: "int", nullable: true),
                    ResourceType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDemandSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDemandSubmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Ability = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MPerpetualScheduleTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ScheduleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MPerpetualScheduleTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DemandDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RandomSeed = table.Column<int>(type: "int", nullable: false),
                    WorkerCount = table.Column<int>(type: "int", nullable: false),
                    SeedCount = table.Column<int>(type: "int", nullable: false),
                    TimeLimitSeconds = table.Column<int>(type: "int", nullable: false),
                    ProgramVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InputHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InputSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerpetualScheduleJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultDetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadedPreviousSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParsedScheduleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedPreviousSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false)
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false)
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
                name: "StandardShiftTimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Shift = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardShiftTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardShiftTimes_ConfigurationRevisions_ConfigurationRevisionId",
                        column: x => x.ConfigurationRevisionId,
                        principalTable: "ConfigurationRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeDemandSubmissionAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedRest = table.Column<bool>(type: "bit", nullable: false),
                    Station = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventDescription = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDemandSubmissionAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeDemandSubmissionAssignments_EmployeeDemandSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "EmployeeDemandSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CandidateIndex = table.Column<int>(type: "int", nullable: true),
                    SourceStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HasErrors = table.Column<bool>(type: "bit", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousAdoptedScheduleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedPreviousScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfigurationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PerpetualScheduleJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerpetualScheduleFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerpetualScheduleUploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevisionToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RestIntervalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
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
                    Workspace = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdoptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdoptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Station = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Affiliation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Ability = table.Column<int>(type: "int", nullable: true),
                    MonthlyShift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OpeningRest = table.Column<int>(type: "int", nullable: true),
                    OpeningSpecialRest = table.Column<int>(type: "int", nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(type: "int", nullable: false),
                    ClosingRest = table.Column<int>(type: "int", nullable: true),
                    ClosingSpecialRest = table.Column<int>(type: "int", nullable: true),
                    NormalWorkCount = table.Column<int>(type: "int", nullable: true),
                    PerpetualScheduleId = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemandDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Affiliation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Ability = table.Column<int>(type: "int", nullable: true),
                    MonthlyShift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OpeningRest = table.Column<int>(type: "int", nullable: true),
                    OpeningSpecialRest = table.Column<int>(type: "int", nullable: true),
                    RequestedLeaveRestCount = table.Column<int>(type: "int", nullable: false),
                    PerpetualScheduleId = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                name: "DemandSubmissionImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemandDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandSubmissionImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandSubmissionImports_DemandDrafts_DemandDraftId",
                        column: x => x.DemandDraftId,
                        principalTable: "DemandDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleEmployeeSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedRest = table.Column<bool>(type: "bit", nullable: false),
                    Station = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventDescription = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DemandEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedRest = table.Column<bool>(type: "bit", nullable: false),
                    Station = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EventDescription = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

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
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AtUtcTicks",
                table: "AuditLogs",
                column: "AtUtcTicks");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_SessionId",
                table: "AuditLogs",
                column: "SessionId");

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
                name: "IX_DemandSubmissionImports_DemandDraftId",
                table: "DemandSubmissionImports",
                column: "DemandDraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDemandSubmissionAssignments_SubmissionId_Date",
                table: "EmployeeDemandSubmissionAssignments",
                columns: new[] { "SubmissionId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDemandSubmissions_Workspace_Month_EmployeeCode",
                table: "EmployeeDemandSubmissions",
                columns: new[] { "Workspace", "Month", "EmployeeCode" },
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

            migrationBuilder.CreateIndex(
                name: "IX_StandardShiftTimes_ConfigurationRevisionId_Workspace_Shift",
                table: "StandardShiftTimes",
                columns: new[] { "ConfigurationRevisionId", "Workspace", "Shift" },
                unique: true);
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
                name: "DemandSubmissionImports");

            migrationBuilder.DropTable(
                name: "EmployeeDemandSubmissionAssignments");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "ExternalAssignments");

            migrationBuilder.DropTable(
                name: "MPerpetualScheduleTemplates");

            migrationBuilder.DropTable(
                name: "NationalHolidays");

            migrationBuilder.DropTable(
                name: "NonStandardShifts");

            migrationBuilder.DropTable(
                name: "ScheduleAssignments");

            migrationBuilder.DropTable(
                name: "StandardShiftTimes");

            migrationBuilder.DropTable(
                name: "WorkspacePermissions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "DemandEmployees");

            migrationBuilder.DropTable(
                name: "EmployeeDemandSubmissions");

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
