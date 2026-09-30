using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Infrastructure.Identity;
using Infrastructure.MultiTenancy;
using Infrastructure.Payments;
using Infrastructure.Persistence;
using Infrastructure.Seeding;
using Infrastructure.Sms;
using Infrastructure.Webhooks;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddSingleton<ITenantCatalog, TenantCatalog>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICustomerLinks, CustomerLinks>();

        services.AddScoped<TenantSaveInterceptor>();
        services.AddScoped<TenantSessionInterceptor>();
        // Read from the final configuration when the context is built, not at registration, so every source -
        // appsettings.Local.json, environment variables, a test host's overrides - is already applied
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseSqlServer(provider.GetRequiredService<IConfiguration>().GetConnectionString("Database")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Database is not configured. Put it in src/Web/appsettings.Local.json."))
            .AddInterceptors(
                provider.GetRequiredService<TenantSaveInterceptor>(),
                provider.GetRequiredService<TenantSessionInterceptor>())
            .ConfigureWarnings(warnings => warnings.Ignore(
                // Identity's claim/role/login rows hang off the filtered user; they are only ever read through it
                CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning,
                // The outbox save runs in a transaction that is rolled back whole when a save fails, so it never
                // needs the savepoints MARS turns off
                SqlServerEventId.SavepointsDisabledBecauseOfMARS)));
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddSingleton<SmsLog>();
        services.AddSingleton<ISmsSender, FakeSmsSender>();
        services.AddSingleton<FakePaymentLog>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddSingleton<FakePayoutLog>();
        services.AddSingleton<IPayoutGateway, FakePayoutGateway>();

        services
            .AddHttpClient<IWebhookSender, HttpWebhookSender>((provider, client) => client.Timeout = TimeSpan.Parse(
                provider.GetRequiredService<IConfiguration>()["Webhooks:Timeout"]
                    ?? throw new InvalidOperationException("Webhooks:Timeout is not set."),
                CultureInfo.InvariantCulture))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton<DemoDataSeeder>();

        return services;
    }

    /// <summary>Identity stores and options. The web host adds the cookie scheme on top.</summary>
    public static IdentityBuilder AddAppIdentity(this IServiceCollection services)
    {
        return services
            .AddIdentity<AppUser, AppRole>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddClaimsPrincipalFactory<AppClaimsFactory>()
            .AddDefaultTokenProviders();
    }
}
