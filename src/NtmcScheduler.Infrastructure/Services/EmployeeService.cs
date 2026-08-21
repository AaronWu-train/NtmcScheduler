using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class EmployeeService(IDbContextFactory<NtmcDbContext> dbFactory) : IEmployeeService
{
    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var canViewAbility = workspace.IsMaintenance() && actor.CanEdit(workspace);
        return await db.Employees.AsNoTracking()
            .Where(x => x.Workspace == workspace)
            .OrderBy(x => x.EmployeeCode)
            .Select(x => new EmployeeDto(x.Id, x.Workspace, x.EmployeeCode, x.Name, x.Affiliation, x.EmploymentStartDate,
                canViewAbility ? x.Ability : null, x.RevisionToken))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeDto> SaveAsync(SaveEmployeeCommand command, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, command.Workspace);
        Validate(command);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Employee employee;
        object? before = null;
        if (command.Id is null)
        {
            employee = new Employee { Workspace = command.Workspace, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Employees.Add(employee);
        }
        else
        {
            employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
                ?? throw new DomainValidationException("找不到員工。");
            if (employee.Workspace != command.Workspace) throw new ForbiddenOperationException("工作區不符。");
            if (command.RevisionToken != employee.RevisionToken) throw new ConcurrencyConflictException("員工資料已被其他人修改，請重新整理。");
            before = ServiceSupport.ToDto(employee);
        }
        employee.EmployeeCode = command.EmployeeCode.Trim();
        employee.Name = command.Name.Trim();
        employee.Affiliation = command.Affiliation.Trim();
        employee.EmploymentStartDate = command.EmploymentStartDate;
        employee.Ability = command.Ability;
        employee.UpdatedAtUtc = DateTimeOffset.UtcNow;
        employee.RevisionToken = Guid.NewGuid();
        ServiceSupport.AddAudit(db, actor, command.Id is null ? "EmployeeCreated" : "EmployeeUpdated", command.Workspace, "Employee", employee.Id, before, ServiceSupport.ToDto(employee));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceSupport.ToDto(employee);
    }

    public async Task DeleteAsync(Guid id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainValidationException("找不到員工。");
        ServiceSupport.RequireEditor(actor, employee.Workspace);
        if (employee.RevisionToken != revisionToken) throw new ConcurrencyConflictException("員工資料已被其他人修改，請重新整理。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new
        {
            employee.Id,
            employee.Workspace,
            employee.EmployeeCode,
            employee.Name,
            employee.Affiliation,
            employee.EmploymentStartDate,
            employee.Ability,
            employee.CreatedAtUtc,
            employee.UpdatedAtUtc,
            employee.RevisionToken
        };
        db.Employees.Remove(employee);
        ServiceSupport.AddAudit(db, actor, "EmployeeDeleted", employee.Workspace, "Employee", employee.Id, before, null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<EmployeeImportPreviewDto> PreviewImportAsync(
        WorkspaceCode workspace,
        Stream csv,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, workspace);
        try
        {
            var records = await UploadFile.ParseAsync(csv, path => ParseImport(path, workspace), cancellationToken);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.Employees.AsNoTracking().Where(x => x.Workspace == workspace).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);
            var existingByCode = existing.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
            var differences = records.Select(record => existingByCode.TryGetValue(record.EmployeeCode, out var employee)
                ? Same(record, employee) ? $"不變：{record.EmployeeCode} {record.Name}" : $"更新：{record.EmployeeCode} {record.Name}"
                : $"新增：{record.EmployeeCode} {record.Name}").ToArray();
            return new(true, [], [.. differences, "CSV 未出現的既有員工不會被刪除。"], SnapshotToken(existing));
        }
        catch (Exception exception) when (exception is DomainValidationException or MalformedLineException or IOException)
        {
            return new(false, [exception.Message], [], Guid.Empty);
        }
    }

    public async Task ImportAsync(
        WorkspaceCode workspace,
        Stream csv,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, workspace);
        var records = await UploadFile.ParseAsync(csv, path => ParseImport(path, workspace), cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Employees.Where(x => x.Workspace == workspace).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);
        if (revisionToken != SnapshotToken(existing))
            throw new ConcurrencyConflictException("員工清單在預覽後已被修改，請重新預覽。");
        var existingByCode = existing.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!existingByCode.TryGetValue(record.EmployeeCode, out var employee))
            {
                employee = new Employee { Workspace = workspace, EmployeeCode = record.EmployeeCode, CreatedAtUtc = DateTimeOffset.UtcNow };
                db.Employees.Add(employee);
            }
            employee.Name = record.Name;
            employee.Affiliation = record.Affiliation;
            employee.EmploymentStartDate = record.EmploymentStartDate;
            employee.Ability = record.Ability;
            employee.UpdatedAtUtc = DateTimeOffset.UtcNow;
            employee.RevisionToken = Guid.NewGuid();
        }
        ServiceSupport.AddAudit(db, actor, "EmployeeCsvImported", workspace, "EmployeeList", workspace,
            new { Existing = existing.Count }, new { Imported = records.Count });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void Validate(SaveEmployeeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.EmployeeCode) || command.EmployeeCode.Trim().Length > 32 ||
            string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(command.Affiliation) || command.Affiliation.Trim().Length > 64)
            throw new DomainValidationException("員工 ID、姓名與所屬必填，且長度不得超過限制。");
        if (command.Workspace.IsStation())
        {
            if (command.Ability is not null || !command.Workspace.Stations().Contains(command.Affiliation, StringComparer.Ordinal))
                throw new DomainValidationException($"{command.Workspace.DisplayName()}員工所屬必須為 {command.Workspace.Stations()[0]}–{command.Workspace.Stations()[command.Workspace.Stations().Count - 1]}，能力必須留空。");
        }
        else if (command.Ability is < 1 or > 5)
            throw new DomainValidationException("T 員工能力必須為 1–5。");
    }

    private static IReadOnlyList<EmployeeImportRecord> ParseImport(string path, WorkspaceCode workspace)
    {
        var expected = workspace.IsStation()
            ? new[] { "ID", "姓名", "所屬車站", "月中開始排班日" }
            : new[] { "ID", "姓名", "所屬", "月中開始排班日", "能力" };
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = IgnoreTrailingEmptyFields(parser.ReadFields() ?? [], expected.Length);
        if (header.Length > 3 && header[3] == "到職日期") header[3] = "月中開始排班日";
        var validHeader = header.SequenceEqual(expected) || workspace.IsMaintenance() && header.SequenceEqual(["ID", "姓名", "專業分組", "月中開始排班日", "能力"]);
        if (!validHeader) throw new DomainValidationException($"員工 CSV 表頭必須為：{string.Join(',', expected)}");
        var records = new List<EmployeeImportRecord>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            fields = IgnoreTrailingEmptyFields(fields, expected.Length);
            if (fields.Length != expected.Length) throw new DomainValidationException($"員工 CSV 第 {parser.LineNumber - 1} 列欄數錯誤。");
            var code = fields[0].Trim();
            var name = fields[1].Trim();
            var affiliation = fields[2].Trim();
            var employmentStartText = fields[3].Trim();
            DateOnly? employmentStart = null;
            if (employmentStartText.Length > 0)
            {
                if (!DateOnly.TryParseExact(employmentStartText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEmploymentStart))
                    throw new DomainValidationException($"員工 {code} 的月中開始排班日必須使用 yyyy-MM-dd。");
                employmentStart = parsedEmploymentStart;
            }
            int? ability = null;
            if (workspace.IsMaintenance())
            {
                if (!int.TryParse(fields[4].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAbility) || parsedAbility is < 1 or > 5)
                    throw new DomainValidationException($"員工 {code} 的能力必須為 1–5。");
                ability = parsedAbility;
            }
            var command = new SaveEmployeeCommand(null, workspace, code, name, affiliation, employmentStart, ability, null);
            Validate(command);
            if (!codes.Add(code)) throw new DomainValidationException($"員工 ID {code} 不可重複。");
            records.Add(new(code, name, affiliation, employmentStart, ability));
        }
        if (records.Count == 0) throw new DomainValidationException("員工 CSV 沒有資料列。");
        return records;
    }

    private static string[] IgnoreTrailingEmptyFields(string[] fields, int expectedCount) =>
        fields.Length > expectedCount && fields.Skip(expectedCount).All(string.IsNullOrWhiteSpace)
            ? fields[..expectedCount]
            : fields;

    private static bool Same(EmployeeImportRecord record, Employee employee) => record.Name == employee.Name &&
        record.Affiliation == employee.Affiliation && record.EmploymentStartDate == employee.EmploymentStartDate && record.Ability == employee.Ability;

    private static Guid SnapshotToken(IReadOnlyList<Employee> employees)
    {
        var text = string.Join('\n', employees.OrderBy(x => x.EmployeeCode).Select(x => $"{x.Id:N}|{x.RevisionToken:N}"));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }

    private sealed record EmployeeImportRecord(string EmployeeCode, string Name, string Affiliation, DateOnly? EmploymentStartDate, int? Ability);
}
