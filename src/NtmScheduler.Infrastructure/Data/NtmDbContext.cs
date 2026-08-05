using Microsoft.EntityFrameworkCore;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Data;

public sealed class NtmDbContext : DbContext
{
    public NtmDbContext(DbContextOptions<NtmDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeMonthlyShift> EmployeeMonthlyShifts => Set<EmployeeMonthlyShift>();
    public DbSet<FixedEvent> FixedEvents => Set<FixedEvent>();
    public DbSet<ScheduleCycle> ScheduleCycles => Set<ScheduleCycle>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<CandidateSolution> CandidateSolutions => Set<CandidateSolution>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<MonthSchedule> MonthSchedules => Set<MonthSchedule>();
    public DbSet<ScheduleEdit> ScheduleEdits => Set<ScheduleEdit>();
    public DbSet<ScheduleSnapshot> ScheduleSnapshots => Set<ScheduleSnapshot>();
    public DbSet<RuleSetting> RuleSettings => Set<RuleSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.HomeStation).HasMaxLength(16);
            e.Property(x => x.Specialty).HasMaxLength(64);
        });

        modelBuilder.Entity<EmployeeMonthlyShift>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.Month });
            e.Property(x => x.EmployeeId).HasMaxLength(32);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Shift).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Employee)
                .WithMany(x => x.MonthlyShifts)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FixedEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(32);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.EmployeeId, x.Type, x.Date });
            e.HasOne(x => x.Employee)
                .WithMany(x => x.FixedEvents)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleCycle>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Start).IsUnique();
            e.Property(x => x.RequiredR).HasDefaultValue(16);
        });

        modelBuilder.Entity<ScheduleRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.TargetMonth).HasMaxLength(7);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ScheduleStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.OptimizationStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ProgramVersion).HasMaxLength(64);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.HasIndex(x => new { x.Unit, x.TargetMonth, x.CreatedAt });
        });

        modelBuilder.Entity<CandidateSolution>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RunId, x.Index }).IsUnique();
            e.HasOne(x => x.Run)
                .WithMany(x => x.Candidates)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.EmployeeId).HasMaxLength(32);
            e.Property(x => x.State).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.OwnerType, x.OwnerId, x.EmployeeId, x.Date }).IsUnique();
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });

        modelBuilder.Entity<MonthSchedule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.HasIndex(x => new { x.Unit, x.Month }).IsUnique();
            e.HasOne(x => x.SourceRun)
                .WithMany(x => x.SourcedSchedules)
                .HasForeignKey(x => x.SourceRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SourceCandidate)
                .WithMany()
                .HasForeignKey(x => x.SourceCandidateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ScheduleEdit>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(32);
            e.Property(x => x.BeforeState).HasMaxLength(32);
            e.Property(x => x.AfterState).HasMaxLength(32);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.HasIndex(x => new { x.ScheduleId, x.Seq }).IsUnique();
            e.HasOne(x => x.Schedule)
                .WithMany(x => x.Edits)
                .HasForeignKey(x => x.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.HasIndex(x => new { x.Unit, x.Month, x.VersionNo }).IsUnique();
            e.HasIndex(x => new { x.Unit, x.Month, x.IsCurrent });
        });

        modelBuilder.Entity<RuleSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.RuleId).HasMaxLength(64);
            e.HasIndex(x => new { x.Unit, x.RuleId }).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.TargetType).HasMaxLength(64);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.TargetType, x.TargetId });
        });
    }
}
