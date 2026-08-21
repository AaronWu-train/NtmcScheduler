using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Infrastructure;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Services;
using NtmcScheduler.Web.Components;
using NtmcScheduler.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole();
}
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var secureCookies = !builder.Environment.IsDevelopment();
builder.Services.AddSingleton<ScheduleRunNotifier>();
builder.Services.AddSingleton<IScheduleRunNotifier>(services => services.GetRequiredService<ScheduleRunNotifier>());
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = secureCookies ? "__Host-NtmcAntiforgery" : "NtmcAntiforgery-Dev";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentActorService>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
builder.Services.AddNtmcInfrastructure(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("NtmcScheduler.Migrations.SqlServer"));
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("NtmcScheduler.Migrations.Sqlite"));
    else throw new InvalidOperationException("DatabaseProvider must be Sqlite or SqlServer.");
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies(options =>
    {
        options.ApplicationCookie!.Configure(cookie =>
        {
            cookie.Cookie.Name = secureCookies ? "__Host-NtmcScheduler" : "NtmcScheduler-Dev";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            cookie.LoginPath = "/Account/Login";
            cookie.AccessDeniedPath = "/Account/AccessDenied";
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            cookie.SlidingExpiration = true;
        });
    });

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 2;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        // TODO: Revisit and strengthen the password policy before production deployment.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<NtmcDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, NtmcClaimsPrincipalFactory>();
builder.Services.AddAuthorization();

var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (string.IsNullOrWhiteSpace(keyPath)) throw new InvalidOperationException("DataProtection:KeyPath is required.");
var keyDirectory = Path.GetFullPath(keyPath, builder.Environment.ContentRootPath);
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("NtmcScheduler")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
if (!string.IsNullOrWhiteSpace(certificatePath))
{
    var password = builder.Configuration["DataProtection:CertificatePassword"];
    var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
    dataProtection.ProtectKeysWithCertificate(certificate);
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Production requires DataProtection:CertificatePath so persisted keys are encrypted at rest.");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var value in builder.Configuration.GetSection("KnownProxies").Get<string[]>() ?? [])
        if (IPAddress.TryParse(value, out var address)) options.KnownProxies.Add(address);
});

var app = builder.Build();
await InitializeDatabaseAsync(app.Services);
if (args is ["--init-admin", var initialUserName])
{
    await InitializeAdministratorAsync(app.Services, initialUserName);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return Task.CompletedTask;
    });
    await next();
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var mustChangePassword = context.User.Identity?.IsAuthenticated == true &&
        context.User.FindFirstValue("must_change_password") == "true";
    var allowed = context.Request.Path.StartsWithSegments("/Account/ChangePassword") ||
        context.Request.Path.StartsWithSegments("/Account/Logout") ||
        context.Request.Path.StartsWithSegments("/_framework") ||
        context.Request.Path == "/app.css";
    if (mustChangePassword && !allowed)
    {
        context.Response.Redirect("/Account/ChangePassword");
        return;
    }
    await next();
});
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/Account/Logout", async (HttpContext context, SignInManager<ApplicationUser> signInManager, NtmcDbContext db, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    if (context.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
    {
        var actor = await CreateHttpActorAsync(context.User, context, db, context.RequestAborted);
        AuditWriter.Add(db, actor, "LogoutSucceeded", null, "Authentication", userId, null, null);
        await db.SaveChangesAsync();
    }
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("~/Account/Login");
}).RequireAuthorization();

app.MapGet("/download/schedules/{versionId:guid}.csv", async (
    Guid versionId,
    HttpContext context,
    NtmcDbContext db,
    IScheduleService schedules,
    CancellationToken cancellationToken) =>
{
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var bytes = await schedules.ExportCsvAsync(versionId, actor, cancellationToken);
    return Results.File(bytes, "text/csv; charset=utf-8", $"schedule-{versionId:N}.csv");
}).RequireAuthorization();

app.MapGet("/download/schedules/{versionId:guid}-external.csv", async (
    Guid versionId,
    HttpContext context,
    NtmcDbContext db,
    IScheduleService schedules,
    CancellationToken cancellationToken) =>
{
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var bytes = await schedules.ExportExternalCsvAsync(versionId, actor, cancellationToken);
    return Results.File(bytes, "text/csv; charset=utf-8", $"schedule-{versionId:N}-external.csv");
}).RequireAuthorization();

app.MapGet("/demands/{demandId:guid}/perpetual.csv", async (
    Guid demandId,
    HttpContext context,
    NtmcDbContext db,
    IDemandService demands,
    CancellationToken cancellationToken) =>
{
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var file = await demands.ExportPerpetualScheduleAsync(demandId, actor, cancellationToken);
    return Results.Text(Encoding.UTF8.GetString(file.Content), "text/plain; charset=utf-8");
}).RequireAuthorization();

app.MapGet("/download/demands/{demandId:guid}/perpetual.csv", async (
    Guid demandId,
    HttpContext context,
    NtmcDbContext db,
    IDemandService demands,
    CancellationToken cancellationToken) =>
{
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var file = await demands.ExportPerpetualScheduleAsync(demandId, actor, cancellationToken);
    return Results.File(file.Content, "text/csv; charset=utf-8", file.FileName);
}).RequireAuthorization();

app.MapGet("/download/demands/{demandId:guid}/previous.csv", async (
    Guid demandId,
    HttpContext context,
    NtmcDbContext db,
    IDemandService demands,
    CancellationToken cancellationToken) =>
{
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var file = await demands.ExportPreviousScheduleAsync(demandId, actor, cancellationToken);
    return Results.File(file.Content, "text/csv; charset=utf-8", file.FileName);
}).RequireAuthorization();

app.MapGet("/{workspace}/perpetual.csv", async (
    string workspace,
    HttpContext context,
    NtmcDbContext db,
    IMPerpetualScheduleService perpetual,
    CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<WorkspaceCode>(workspace, true, out var workspaceCode) || !workspaceCode.IsStation()) return Results.NotFound();
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var file = await perpetual.ExportAsync(workspaceCode, actor, cancellationToken);
    return Results.Text(Encoding.UTF8.GetString(file.Content), "text/plain; charset=utf-8");
}).RequireAuthorization();

app.MapGet("/download/{workspace}/perpetual.csv", async (
    string workspace,
    HttpContext context,
    NtmcDbContext db,
    IMPerpetualScheduleService perpetual,
    CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<WorkspaceCode>(workspace, true, out var workspaceCode) || !workspaceCode.IsStation()) return Results.NotFound();
    var actor = await CreateHttpActorAsync(context.User, context, db, cancellationToken);
    var file = await perpetual.ExportAsync(workspaceCode, actor, cancellationToken);
    return Results.File(file.Content, "text/csv; charset=utf-8", file.FileName);
}).RequireAuthorization();

app.MapGet("/download/templates/{workspace}/{kind}.csv", (string workspace, string kind) =>
{
    if (!Enum.TryParse<WorkspaceCode>(workspace, true, out var workspaceCode)) return Results.NotFound();
    var header = kind.ToLowerInvariant() switch
    {
        "employees" when workspaceCode.IsStation() => "ID,姓名,所屬車站,月中開始排班日",
        "employees" when workspaceCode == WorkspaceCode.T => "ID,姓名,所屬,月中開始排班日,能力",
        "demand" or "previous" => ScheduleCsv.MonthlyDownloadHeader(workspaceCode),
        "perpetual" when workspaceCode.IsStation() => ScheduleCsv.MPerpetualHeader,
        "rest-intervals" => "區間開始日期,區間結束日期,國定假日日期",
        "non-standard-shifts" => "班型,時間,代碼",
        _ => null
    };
    return header is null
        ? Results.NotFound()
        : Results.File(Encoding.UTF8.GetBytes('\uFEFF' + header + Environment.NewLine), "text/csv; charset=utf-8", $"{workspaceCode.ToString().ToLowerInvariant()}-{kind}-template.csv");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
    await db.Database.MigrateAsync();
    if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync("Administrator"))
    {
        var result = await roles.CreateAsync(new IdentityRole<Guid>("Administrator"));
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}

static async Task InitializeAdministratorAsync(IServiceProvider services, string userName)
{
    if (string.IsNullOrWhiteSpace(userName)) throw new InvalidOperationException("Usage: --init-admin USERNAME");
    Console.Write("Temporary password: ");
    var password = ReadSecret();
    await using var scope = services.CreateAsyncScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    if (await users.FindByNameAsync(userName) is not null) throw new InvalidOperationException("User already exists.");
    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = userName.Trim(), MustChangePassword = true };
    var created = await users.CreateAsync(user, password);
    if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
    var role = await users.AddToRoleAsync(user, "Administrator");
    if (!role.Succeeded) throw new InvalidOperationException(string.Join("; ", role.Errors.Select(x => x.Description)));
    var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
    var actor = new ActorContext(user.Id, user.UserName!, true, new HashSet<WorkspaceCode>(), "init-admin");
    AuditWriter.Add(db, actor, "InitialAdministratorCreated", null, "User", user.Id, null, null);
    await db.SaveChangesAsync();
    Console.WriteLine($"Administrator '{user.UserName}' created. The password must be changed at first login.");
}

static string ReadSecret()
{
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var characters = new List<char>();
    while (Console.ReadKey(intercept: true) is { } key && key.Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && characters.Count > 0) characters.RemoveAt(characters.Count - 1);
        else if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
    }
    Console.WriteLine();
    return new string(characters.ToArray());
}

static async Task<ActorContext> CreateHttpActorAsync(ClaimsPrincipal principal, HttpContext context, NtmcDbContext db, CancellationToken cancellationToken)
{
    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        throw new UnauthorizedAccessException();
    var workspaces = await db.WorkspacePermissions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Workspace).ToListAsync(cancellationToken);
    return new(userId, principal.Identity?.Name ?? "unknown", principal.IsInRole("Administrator"), workspaces.ToHashSet(),
        context.TraceIdentifier, ActorContextFactory.ParseSessionId(principal), context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(),
        principal.FindFirstValue("must_change_password") == "true");
}

public partial class Program;
