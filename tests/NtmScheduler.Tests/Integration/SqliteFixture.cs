using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Services;

namespace NtmScheduler.Tests.Integration;

internal sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public NtmDbContext Db { get; }
    public AuditWriter Audit { get; }
    public EmployeeService Employees { get; }
    public HistoryImportService HistoryImport { get; }
    public ExportService Export { get; }

    private SqliteFixture(SqliteConnection connection, NtmDbContext db)
    {
        _connection = connection;
        Db = db;
        Audit = new AuditWriter(db);
        Employees = new EmployeeService(db, Audit);
        HistoryImport = new HistoryImportService(db, Audit);
        Export = new ExportService(db);
    }

    public static async Task<SqliteFixture> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NtmDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new NtmDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteFixture(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
