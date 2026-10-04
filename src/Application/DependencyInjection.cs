using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Application.Auth.PhoneLogin;
using Application.Customers;
using Application.Delivery.Door;
using Application.Delivery.HubCash;
using Application.Delivery.HubTrips;
using Application.Delivery.PlanTrips;
using Application.Delivery.RiderDay;
using Application.Grouping;
using Application.Grouping.CombineDeliveries;
using Application.Grouping.CustomerDeliveries;
using Application.Grouping.LockDueGroups;
using Application.Grouping.ShipNow;
using Application.Merchants;
using Application.Merchants.ShopWindow;
using Application.Merchants.Webhook;
using Application.Network.HubScan;
using Application.Network.ListAreas;
using Application.Network.PickupRoutes;
using Application.Operations.Dashboard;
using Application.Notifications.SendOutbox;
using Application.Notifications.SendWebhooks;
using Application.Orders.ConfirmOrder;
using Application.Orders.CreateOrder;
using Application.Orders.GetOrder;
using Application.Orders.PackageLabels;
using Application.Payments.MerchantPayouts;
using Application.Payments.SettleMerchants;
using Application.Pricing.GetQuote;

namespace Application;

public static class DependencyInjection
{
    /// <summary>Handlers are plain scoped classes, injected where they are used. No mediator.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        services.AddScoped<CustomerDirectory>();
        services.AddScoped<DeliveryGrouping>();
        services.AddScoped<LockDueGroupsJob>();
        services.AddScoped<SendOutboxJob>();
        services.AddScoped<CustomerTexts>();
        services.AddScoped<SendWebhooksJob>();
        services.AddScoped<MerchantWebhooks>();
        services.AddScoped<MerchantWebhookHandler>();
        services.AddScoped<ShoppingWindow>();
        services.AddScoped<MerchantShopWindowHandler>();
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ConfirmOrderHandler>();
        services.AddScoped<GetQuoteHandler>();
        services.AddScoped<ListAreasHandler>();
        services.AddScoped<ShipNowHandler>();
        services.AddScoped<CombineDeliveriesHandler>();
        services.AddScoped<CustomerDeliveriesHandler>();
        services.AddScoped<PickupRoutesHandler>();
        services.AddScoped<HubScanHandler>();
        services.AddScoped<PackageLabelsHandler>();
        services.AddScoped<TripPlanning>();
        services.AddScoped<PlanTripsJob>();
        services.AddScoped<HubTripsHandler>();
        services.AddScoped<OperationsDashboardHandler>();
        services.AddScoped<RiderDayHandler>();
        services.AddScoped<DoorHandler>();
        services.AddScoped<HubCashHandler>();
        services.AddScoped<SettleMerchantsJob>();
        services.AddScoped<MerchantPayoutsHandler>();
        services.AddScoped<ShopDropOffs>();
        services.AddScoped<PhoneLoginService>();

        return services;
    }
}
