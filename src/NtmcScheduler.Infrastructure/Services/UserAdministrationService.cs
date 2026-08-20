using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var users = await db.Users.AsNoTracking().Include(x => x.WorkspacePermissions).OrderBy(x => x.UserName).ToListAsync(cancellationToken);
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
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = command.UserName.Trim(), MustChangePassword = true };
        var result = await userManager.CreateAsync(user, command.TemporaryPassword);
        if (!result.Succeeded) throw new DomainValidationException(string.Join("；", result.Errors.Select(x => x.Description)));
        await EnsureAdministratorRoleAsync(roleManager);
        if (command.IsAdministrator) await userManager.AddToRoleAsync(user, "Administrator");
        foreach (var workspace in command.EditableWorkspaces)
            db.WorkspacePermissions.Add(new() { UserId = user.Id, Workspace = workspace });
        ServiceSupport.AddAudit(db, actor, "UserCreated", null, "User", user.Id, null,
            new { user.UserName, command.IsAdministrator, Workspaces = command.EditableWorkspaces });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(user.Id, user.UserName, false, true, command.IsAdministrator, command.EditableWorkspaces, Revision(user));
    }

    public async Task ResetPasswordAsync(Guid userId, string temporaryPassword, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new DomainValidationException("找不到使用者。");
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
        var user = await db.Users.Include(x => x.WorkspacePermissions).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
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

    private static void ValidateUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > 100)
            throw new DomainValidationException("帳號必填且不可超過 100 字元。");
    }

    private static Guid Revision(ApplicationUser user) => Guid.TryParse(user.ConcurrencyStamp, out var token)
        ? token
        : throw new InvalidOperationException("Identity concurrency stamp is not a GUID.");
}
