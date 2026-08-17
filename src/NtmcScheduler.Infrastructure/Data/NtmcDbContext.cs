using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NtmcScheduler.Infrastructure.Data;

public sealed class NtmcDbContext(DbContextOptions<NtmcDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<WorkspacePermission> WorkspacePermissions => Set<WorkspacePermission>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ConfigurationRevision> ConfigurationRevisions => Set<ConfigurationRevision>();
    public DbSet<CurrentConfiguration> CurrentConfigurations => Set<CurrentConfiguration>();
    public DbSet<RestIntervalEntity> RestIntervals => Set<RestIntervalEntity>();
    public DbSet<NationalHoliday> NationalHolidays => Set<NationalHoliday>();
    public DbSet<NonStandardShiftEntity> NonStandardShifts => Set<NonStandardShiftEntity>();
    public DbSet<DemandDraft> DemandDrafts => Set<DemandDraft>();
    public DbSet<DemandEmployee> DemandEmployees => Set<DemandEmployee>();
    public DbSet<DemandAssignment> DemandAssignments => Set<DemandAssignment>();
    public DbSet<MPerpetualScheduleTemplate> MPerpetualScheduleTemplates => Set<MPerpetualScheduleTemplate>();
    public DbSet<UploadedPreviousSchedule> UploadedPreviousSchedules => Set<UploadedPreviousSchedule>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<ScheduleVersion> ScheduleVersions => Set<ScheduleVersion>();
    public DbSet<ScheduleEmployeeSnapshot> ScheduleEmployeeSnapshots => Set<ScheduleEmployeeSnapshot>();
    public DbSet<ScheduleAssignment> ScheduleAssignments => Set<ScheduleAssignment>();
    public DbSet<ExternalAssignment> ExternalAssignments => Set<ExternalAssignment>();
    public DbSet<AdoptedSchedule> AdoptedSchedules => Set<AdoptedSchedule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.UserName).HasMaxLength(100);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(100);
        });

        modelBuilder.Entity<WorkspacePermission>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.Workspace });
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.HasOne(x => x.User).WithMany(x => x.WorkspacePermissions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.Property(x => x.EmployeeCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Affiliation).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RevisionToken).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Workspace, x.EmployeeCode }).IsUnique();
        });

        modelBuilder.Entity<ConfigurationRevision>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Version).IsUnique();
        });
        modelBuilder.Entity<CurrentConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RevisionToken).IsConcurrencyToken();
            entity.HasOne(x => x.ConfigurationRevision).WithMany().HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RestIntervalEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ConfigurationRevisionId, x.Start }).IsUnique();
            entity.HasOne(x => x.ConfigurationRevision).WithMany(x => x.RestIntervals).HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NationalHoliday>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.RestIntervalId, x.Date }).IsUnique();
            entity.HasOne(x => x.RestInterval).WithMany(x => x.NationalHolidays).HasForeignKey(x => x.RestIntervalId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NonStandardShiftEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(50);
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.ConfigurationRevisionId, x.Code }).IsUnique();
            entity.HasOne(x => x.ConfigurationRevision).WithMany(x => x.NonStandardShifts).HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DemandDraft>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.Property(x => x.PreviousSource).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.RevisionToken).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Workspace, x.Month }).IsUnique();
            entity.HasOne(x => x.ConfigurationRevision).WithMany().HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.UploadedPreviousSchedule).WithMany().HasForeignKey(x => x.UploadedPreviousScheduleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MPerpetualScheduleTemplate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.RevisionToken).IsConcurrencyToken();
        });
        modelBuilder.Entity<DemandEmployee>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Affiliation).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.DemandDraftId, x.EmployeeCode }).IsUnique();
            entity.HasOne(x => x.DemandDraft).WithMany(x => x.Employees).HasForeignKey(x => x.DemandDraftId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DemandAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DemandEmployeeId, x.Date }).IsUnique();
            entity.HasOne(x => x.DemandEmployee).WithMany(x => x.Assignments).HasForeignKey(x => x.DemandEmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UploadedPreviousSchedule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
        });

        modelBuilder.Entity<ScheduleRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ProgramVersion).HasMaxLength(100);
            entity.Property(x => x.InputHash).HasMaxLength(128);
            entity.Property(x => x.RequestedByName).HasMaxLength(100);
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.HasIndex(x => new { x.Workspace, x.Month, x.CreatedAtUtc });
        });
        modelBuilder.Entity<ScheduleVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.Property(x => x.SourceStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RevisionToken).IsConcurrencyToken();
            entity.HasIndex(x => new { x.Workspace, x.Month, x.CreatedAtUtc });
            entity.HasOne(x => x.SourceRun).WithMany(x => x.Versions).HasForeignKey(x => x.SourceRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ConfigurationRevision).WithMany().HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ScheduleEmployeeSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EmployeeCode).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.ScheduleVersionId, x.EmployeeCode }).IsUnique();
            entity.HasOne(x => x.ScheduleVersion).WithMany(x => x.Employees).HasForeignKey(x => x.ScheduleVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ScheduleAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.ScheduleEmployeeSnapshotId, x.Date }).IsUnique();
            entity.HasOne(x => x.Employee).WithMany(x => x.Assignments).HasForeignKey(x => x.ScheduleEmployeeSnapshotId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ExternalAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.ScheduleVersion).WithMany(x => x.ExternalAssignments).HasForeignKey(x => x.ScheduleVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AdoptedSchedule>(entity =>
        {
            entity.HasKey(x => new { x.Workspace, x.Month });
            entity.Property(x => x.Workspace).HasConversion<string>().HasMaxLength(1);
            entity.HasIndex(x => x.ScheduleVersionId).IsUnique();
            entity.HasOne(x => x.ScheduleVersion).WithMany().HasForeignKey(x => x.ScheduleVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ResourceType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.AtUtcTicks);
            entity.HasIndex(x => new { x.Workspace, x.Action });
        });
    }
}
