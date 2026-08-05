using Microsoft.EntityFrameworkCore;
using NtmScheduler.Infrastructure;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Web.Components;
using NtmScheduler.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Future AD / authentication middleware hook (D-04) — not enabled in v1.
// builder.Services.AddAuthentication(...);
// builder.Services.AddAuthorization(...);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=ntm.db";

builder.Services.AddNtmInfrastructure(connectionString);
builder.Services.AddScoped<OperatorState>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NtmDbContext>();
    // Prefer Migrate when migrations exist; fall back to EnsureCreated for M1/M6 skeleton.
    if (db.Database.GetPendingMigrations().Any())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// app.UseAuthentication();
// app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
