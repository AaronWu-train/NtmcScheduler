using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class UserAdministrationService(IServiceScopeFactory scopes) : IUserAdministrationService
{
    public async Task<IReadOnlyList<UserAccountDto>> ListAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var users = await db.Users.AsNoTracking().Include(x => x.WorkspacePermissions)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.UserName)
            .ToListAsync(cancellationToken);
        var result = new List<UserAccountDto>(users.Count);
        foreach (var user in users)
            result.Add(new(user.Id, user.UserName ?? "", user.IsDisabled, user.MustChangePassword,
                await userManager.IsInRoleAsync(user, "Administrator"),
                user.WorkspacePermissions.Select(x => x.Workspace).ToHashSet(), Revision(user)));
        return result;
    }

    public async Task<UserAccountDto> CreateAsync(CreateUserCommand command, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        ValidateUserName(command.UserName);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorRoleAsync(roleManager);
        var user = await CreateUserAsync(command, actor, db, userManager);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(user.Id, user.UserName ?? "", false, true, command.IsAdministrator, command.EditableWorkspaces, Revision(user));
    }

    public async Task CreateBatchAsync(Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        var commands = await UploadFile.ParseAsync(csv, ParseBatchCsv, cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorRoleAsync(roleManager);
        foreach (var command in commands)
            await CreateUserAsync(command, actor, db, userManager);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        if (userId == actor.UserId) throw new DomainValidationException("不可刪除自己的帳號。");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken)
            ?? throw new DomainValidationException("找不到使用者。");
        if (Revision(user) != revisionToken) throw new ConcurrencyConflictException("帳號已被其他人修改，請重新整理。");
        var before = new { user.UserName, user.IsDisabled, user.IsDeleted };
        user.IsDisabled = true;
        user.IsDeleted = true;
        await userManager.UpdateSecurityStampAsync(user);
        ServiceSupport.AddAudit(db, actor, "UserDeleted", null, "User", user.Id, before,
            new { user.UserName, user.IsDisabled, user.IsDeleted });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid userId, string temporaryPassword, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new DomainValidationException("找不到使用者。");
        if (user.IsDeleted) throw new DomainValidationException("找不到使用者。");
        if (Revision(user) != revisionToken) throw new ConcurrencyConflictException("帳號已被其他人修改，請重新整理。");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!result.Succeeded) throw new DomainValidationException(string.Join("；", result.Errors.Select(x => x.Description)));
        user.MustChangePassword = true;
        await userManager.UpdateSecurityStampAsync(user);
        ServiceSupport.AddAudit(db, actor, "PasswordReset", null, "User", user.Id, null, new { user.UserName, MustChangePassword = true });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Guid userId, bool isDisabled, bool isAdministrator, IReadOnlySet<WorkspaceCode> workspaces, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        if (userId == actor.UserId && (isDisabled || !isAdministrator))
            throw new DomainValidationException("不可停用自己或移除自己的管理者權限。");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.Include(x => x.WorkspacePermissions).SingleOrDefaultAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken)
            ?? throw new DomainValidationException("找不到使用者。");
        if (Revision(user) != revisionToken) throw new ConcurrencyConflictException("帳號已被其他人修改，請重新整理。");
        var before = new { user.UserName, user.IsDisabled, IsAdministrator = await userManager.IsInRoleAsync(user, "Administrator"), Workspaces = user.WorkspacePermissions.Select(x => x.Workspace).ToArray() };
        user.IsDisabled = isDisabled;
        await EnsureAdministratorRoleAsync(roleManager);
        var currentlyAdministrator = await userManager.IsInRoleAsync(user, "Administrator");
        if (isAdministrator && !currentlyAdministrator) await userManager.AddToRoleAsync(user, "Administrator");
        if (!isAdministrator && currentlyAdministrator) await userManager.RemoveFromRoleAsync(user, "Administrator");
        db.WorkspacePermissions.RemoveRange(user.WorkspacePermissions);
        foreach (var workspace in workspaces) db.WorkspacePermissions.Add(new() { UserId = user.Id, Workspace = workspace });
        await userManager.UpdateSecurityStampAsync(user);
        ServiceSupport.AddAudit(db, actor, "UserPermissionsChanged", null, "User", user.Id, before,
            new { user.UserName, user.IsDisabled, isAdministrator, Workspaces = workspaces });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsureAdministratorRoleAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        if (!await roleManager.RoleExistsAsync("Administrator"))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>("Administrator"));
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("；", result.Errors.Select(x => x.Description)));
        }
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        CreateUserCommand command,
        ActorContext actor,
        NtmcDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        ValidateUserName(command.UserName);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = command.UserName.Trim(), MustChangePassword = true };
        var result = await userManager.CreateAsync(user, command.TemporaryPassword);
        if (!result.Succeeded) throw new DomainValidationException(string.Join("；", result.Errors.Select(x => x.Description)));
        if (command.IsAdministrator)
        {
            result = await userManager.AddToRoleAsync(user, "Administrator");
            if (!result.Succeeded) throw new DomainValidationException(string.Join("；", result.Errors.Select(x => x.Description)));
        }
        foreach (var workspace in command.EditableWorkspaces)
            db.WorkspacePermissions.Add(new() { UserId = user.Id, Workspace = workspace });
        ServiceSupport.AddAudit(db, actor, "UserCreated", null, "User", user.Id, null,
            new { user.UserName, command.IsAdministrator, Workspaces = command.EditableWorkspaces });
        return user;
    }

    internal static IReadOnlyList<CreateUserCommand> ParseBatchCsv(string path)
    {
        string[] expected = ["帳號", "一次性密碼", "Administrator", "三鶯M", "三鶯T", "環狀M", "環狀T"];
        using var parser = new TextFieldParser(path, Encoding.UTF8, true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        if (!(parser.ReadFields() ?? []).SequenceEqual(expected))
            throw new DomainValidationException($"帳號 CSV 表頭必須為：{string.Join(',', expected)}");

        var commands = new List<CreateUserCommand>();
        var userNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            var row = parser.LineNumber - 1;
            if (fields.Length != expected.Length)
                throw new DomainValidationException($"帳號 CSV 第 {row} 列欄數錯誤。");
            var userName = fields[0].Trim();
            ValidateUserName(userName);
            if (!userNames.Add(userName))
                throw new DomainValidationException($"帳號 CSV 第 {row} 列的帳號 {userName} 重複。");
            var permissions = fields.Skip(2).Select((value, index) => ParseFlag(value, expected[index + 2], row)).ToArray();
            var workspaces = new HashSet<WorkspaceCode>();
            if (permissions[1]) workspaces.Add(WorkspaceCode.M);
            if (permissions[2]) workspaces.Add(WorkspaceCode.T);
            if (permissions[3]) workspaces.Add(WorkspaceCode.YM);
            if (permissions[4]) workspaces.Add(WorkspaceCode.YT);
            commands.Add(new(userName, fields[1], permissions[0], workspaces));
        }
        if (commands.Count == 0) throw new DomainValidationException("帳號 CSV 沒有資料列。");
        return commands;
    }

    private static bool ParseFlag(string value, string field, long row) => value.Trim() switch
    {
        "1" => true,
        "0" => false,
        _ => throw new DomainValidationException($"帳號 CSV 第 {row} 列的 {field} 必須為 1 或 0。")
    };

    private static void ValidateUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > 100)
            throw new DomainValidationException("帳號必填且不可超過 100 字元。");
    }

    private static Guid Revision(ApplicationUser user) => Guid.TryParse(user.ConcurrencyStamp, out var token)
        ? token
        : throw new InvalidOperationException("Identity concurrency stamp is not a GUID.");
}
