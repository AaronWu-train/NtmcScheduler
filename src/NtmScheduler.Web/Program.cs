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
    scope.ServiceProvider.GetRequiredService<NtmDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts().UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// app.UseAuthentication();
// app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
