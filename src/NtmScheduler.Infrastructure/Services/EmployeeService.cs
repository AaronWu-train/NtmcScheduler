using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using NtmScheduler.Contracts;
using NtmScheduler.Infrastructure.Data;

namespace NtmScheduler.Infrastructure.Services;

public sealed class EmployeeService(NtmDbContext db) : IEmployeeService
{
    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(WorkspaceCode workspace, ActorContext actor, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        return await db.Employees.AsNoTracking()
            .Where(x => x.Workspace == workspace && (includeArchived || !x.IsArchived))
            .OrderBy(x => x.EmployeeCode)
            .Select(x => new EmployeeDto(x.Id, x.Workspace, x.EmployeeCode, x.Name, x.Affiliation, x.EmploymentStartDate, x.Ability, x.IsArchived, x.RevisionToken))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeDto> SaveAsync(SaveEmployeeCommand command, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, command.Workspace);
        Validate(command);
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

    public async Task ArchiveAsync(Guid id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainValidationException("找不到員工。");
        ServiceSupport.RequireEditor(actor, employee.Workspace);
        if (employee.RevisionToken != revisionToken) throw new ConcurrencyConflictException("員工資料已被其他人修改，請重新整理。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = ServiceSupport.ToDto(employee);
        employee.IsArchived = true;
        employee.UpdatedAtUtc = DateTimeOffset.UtcNow;
        employee.RevisionToken = Guid.NewGuid();
        ServiceSupport.AddAudit(db, actor, "EmployeeArchived", employee.Workspace, "Employee", employee.Id, before, ServiceSupport.ToDto(employee));
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
            var existing = await db.Employees.AsNoTracking().Where(x => x.Workspace == workspace).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);
            ValidateArchivedConflicts(records, existing);
            var existingByCode = existing.Where(x => !x.IsArchived).ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
            var differences = records.Select(record => existingByCode.TryGetValue(record.EmployeeCode, out var employee)
                ? Same(record, employee) ? $"不變：{record.EmployeeCode} {record.Name}" : $"更新：{record.EmployeeCode} {record.Name}"
                : $"新增：{record.EmployeeCode} {record.Name}").ToArray();
            return new(true, [], [.. differences, "CSV 未出現的既有員工不會被封存。"], SnapshotToken(existing));
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Employees.Where(x => x.Workspace == workspace).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);
        if (revisionToken != SnapshotToken(existing))
            throw new ConcurrencyConflictException("員工清單在預覽後已被修改，請重新預覽。");
        ValidateArchivedConflicts(records, existing);
        var existingByCode = existing.Where(x => !x.IsArchived).ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
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
            new { Existing = existing.Count(x => !x.IsArchived) }, new { Imported = records.Count });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void Validate(SaveEmployeeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.EmployeeCode) || command.EmployeeCode.Trim().Length > 32 ||
            string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(command.Affiliation) || command.Affiliation.Trim().Length > 64)
            throw new DomainValidationException("員工 ID、姓名與所屬必填，且長度不得超過限制。");
        if (command.Workspace == WorkspaceCode.M)
        {
            if (command.Ability is not null || command.Affiliation is not ("LB01" or "LB02" or "LB03" or "LB04" or "LB05" or "LB06" or "LB07" or "LB08" or "LB09" or "LB10" or "LB11" or "LB12"))
                throw new DomainValidationException("M 員工所屬必須為 LB01–LB12，能力必須留空。");
        }
        else if (command.Ability is < 1 or > 5)
            throw new DomainValidationException("T 員工能力必須為 1–5。");
    }

    private static IReadOnlyList<EmployeeImportRecord> ParseImport(string path, WorkspaceCode workspace)
    {
        var expected = workspace == WorkspaceCode.M
            ? new[] { "ID", "姓名", "所屬車站", "到職日期" }
            : new[] { "ID", "姓名", "專業分組", "到職日期", "能力" };
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? [];
        if (!header.SequenceEqual(expected)) throw new DomainValidationException($"員工 CSV 表頭必須為：{string.Join(',', expected)}");
        var records = new List<EmployeeImportRecord>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            if (fields.Length != expected.Length) throw new DomainValidationException($"員工 CSV 第 {parser.LineNumber - 1} 列欄數錯誤。");
            var code = fields[0].Trim();
            var name = fields[1].Trim();
            var affiliation = fields[2].Trim();
            var employmentStartText = fields[3].Trim();
            DateOnly? employmentStart = null;
            if (employmentStartText.Length > 0)
            {
                if (!DateOnly.TryParseExact(employmentStartText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEmploymentStart))
                    throw new DomainValidationException($"員工 {code} 的到職日期必須使用 yyyy-MM-dd。");
                employmentStart = parsedEmploymentStart;
            }
            int? ability = null;
            if (workspace == WorkspaceCode.T)
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

    private static void ValidateArchivedConflicts(IReadOnlyList<EmployeeImportRecord> records, IReadOnlyList<Employee> employees)
    {
        var archived = employees.Where(x => x.IsArchived).Select(x => x.EmployeeCode).ToHashSet(StringComparer.Ordinal);
        var conflicts = records.Where(x => archived.Contains(x.EmployeeCode)).Select(x => x.EmployeeCode).ToArray();
        if (conflicts.Length > 0) throw new DomainValidationException($"已封存員工不可由匯入自動恢復：{string.Join('、', conflicts)}。");
    }

    private static bool Same(EmployeeImportRecord record, Employee employee) => record.Name == employee.Name &&
        record.Affiliation == employee.Affiliation && record.EmploymentStartDate == employee.EmploymentStartDate && record.Ability == employee.Ability;

    private static Guid SnapshotToken(IReadOnlyList<Employee> employees)
    {
        var text = string.Join('\n', employees.OrderBy(x => x.EmployeeCode).Select(x => $"{x.Id:N}|{x.RevisionToken:N}|{x.IsArchived}"));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }

    private sealed record EmployeeImportRecord(string EmployeeCode, string Name, string Affiliation, DateOnly? EmploymentStartDate, int? Ability);
}
