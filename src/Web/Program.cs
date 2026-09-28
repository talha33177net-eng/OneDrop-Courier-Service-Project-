using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Application;
using Hangfire;
using Infrastructure;
using Infrastructure.Jobs;
using Infrastructure.Seeding;
using Web.Authentication;
using Web.MultiTenancy;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific secrets (the database connection string) live in this git-ignored file, never in the repository.
// Environment variables and command-line arguments are added again after it so they still win.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Tenant} {Message:lj}{NewLine}{Exception}"));

builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection("Tenancy"));
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
var runJobs = builder.Configuration.GetValue<bool>("Jobs:Server");
builder.Services.AddJobs(runJobs);
builder.Services.AddAppIdentity();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "app.auth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
});
builder.Services
    .AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(Policies.Configure);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("otp", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Merchant", Policies.MerchantPortal);
    options.Conventions.AuthorizeFolder("/Customer", Policies.CustomerPortal);
    options.Conventions.AuthorizeFolder("/Platform", Policies.PlatformAdmin);
});

var app = builder.Build();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:DemoData"))
{
    await app.Services
        .GetRequiredService<DemoDataSeeder>()
        .SeedAsync(app.Configuration["Seed:Password"] ?? throw new InvalidOperationException("Seed:Password is not set."));
}

if (runJobs)
{
    app.Services.ScheduleJobs(app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.MapStaticAssets();
app.UseRouting();

// Tenant resolution -> authentication -> tenant/merchant guard -> authorization
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantUserGuardMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapRazorPages().WithStaticAssets();
// Every tenant's jobs in one view, so platform staff only. The policy replaces Hangfire's local-requests-only check
app.MapHangfireDashboardWithAuthorizationPolicy(
    Policies.PlatformAdmin,
    "/jobs",
    new DashboardOptions { Authorization = [], DisplayStorageConnectionString = false, DashboardTitle = "Jobs" });

app.Run();

/// <summary>Visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;
