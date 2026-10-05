using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Application.Dashboards;
using Application.Delivery.Pickups;
using Application.Delivery.RiderDay;
using Application.Delivery.Riders;
using Application.Hubs;
using Application.Hubs.AssignParcels;
using Application.Hubs.HubBoard;
using Application.Hubs.HubOverview;
using Application.Hubs.HubScan;
using Application.Hubs.Runs;
using Application.Merchants.Account;
using Application.Merchants.Admin;
using Application.Merchants.ApiKeys;
using Application.Merchants.Onboarding;
using Application.Merchants.Webhook;
using Application.Network.Coverage;
using Application.Network.ListAreas;
using Application.Notifications.FailedMessages;
using Application.Notifications.SendOutbox;
using Application.Notifications.SendWebhooks;
using Application.Parcels;
using Application.Parcels.Browse;
using Application.Parcels.BulkImport;
using Application.Parcels.CreateParcel;
using Application.Parcels.FraudCheck;
using Application.Parcels.Labels;
using Application.Parcels.ParcelActions;
using Application.Parcels.Quote;
using Application.Parcels.Track;
using Application.Payments.AdminPayouts;
using Application.Payments.MerchantPayments;
using Application.Payments.RunPayouts;
using Application.Pricing.Rates;

namespace Application;

public static class DependencyInjection
{
    /// <summary>Handlers are plain scoped classes, injected where they are used. No mediator.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);

        services.AddScoped<ParcelBooking>();
        services.AddScoped<CreateParcelHandler>();
        services.AddScoped<ParcelListHandler>();
        services.AddScoped<ParcelDetailsHandler>();
        services.AddScoped<ParcelActionsHandler>();
        services.AddScoped<ParcelLabelsHandler>();
        services.AddScoped<BulkImportHandler>();
        services.AddScoped<TrackHandler>();
        services.AddScoped<QuoteHandler>();
        services.AddScoped<FraudCheckHandler>();

        services.AddScoped<HubDirectory>();
        services.AddScoped<HubScanHandler>();
        services.AddScoped<HubBoardHandler>();
        services.AddScoped<HubOverviewHandler>();
        services.AddScoped<AssignParcelsHandler>();
        services.AddScoped<RunsHandler>();

        services.AddScoped<PickupsHandler>();
        services.AddScoped<RiderDayHandler>();
        services.AddScoped<AdminRidersHandler>();

        services.AddScoped<MerchantOnboarding>();
        services.AddScoped<AdminMerchantsHandler>();
        services.AddScoped<MerchantAccountHandler>();
        services.AddScoped<MerchantApiKeysHandler>();
        services.AddScoped<MerchantWebhookHandler>();

        services.AddScoped<PayoutsJob>();
        services.AddScoped<MerchantPaymentsHandler>();
        services.AddScoped<AdminPayoutsHandler>();

        services.AddScoped<RatesHandler>();
        services.AddScoped<ListAreasHandler>();
        services.AddScoped<CoverageHandler>();
        services.AddScoped<DashboardHandler>();

        services.AddScoped<SendOutboxJob>();
        services.AddScoped<RecipientTexts>();
        services.AddScoped<SendWebhooksJob>();
        services.AddScoped<MerchantWebhooks>();
        services.AddScoped<FailedMessagesHandler>();

        return services;
    }
}
