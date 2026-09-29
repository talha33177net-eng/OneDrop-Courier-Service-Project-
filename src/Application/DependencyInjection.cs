using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Application.Auth.PhoneLogin;
using Application.Customers;
using Application.Delivery.Door;
using Application.Delivery.HubTrips;
using Application.Delivery.PlanTrips;
using Application.Delivery.RiderDay;
using Application.Grouping;
using Application.Grouping.CustomerDeliveries;
using Application.Grouping.LockDueGroups;
using Application.Grouping.ShipNow;
using Application.Network.HubScan;
using Application.Network.ListAreas;
using Application.Network.PickupRoutes;
using Application.Notifications.SendOutbox;
using Application.Orders.CreateOrder;
using Application.Orders.GetOrder;
using Application.Orders.PackageLabels;
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
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<GetQuoteHandler>();
        services.AddScoped<ListAreasHandler>();
        services.AddScoped<ShipNowHandler>();
        services.AddScoped<CustomerDeliveriesHandler>();
        services.AddScoped<PickupRoutesHandler>();
        services.AddScoped<HubScanHandler>();
        services.AddScoped<PackageLabelsHandler>();
        services.AddScoped<TripPlanning>();
        services.AddScoped<PlanTripsJob>();
        services.AddScoped<HubTripsHandler>();
        services.AddScoped<RiderDayHandler>();
        services.AddScoped<DoorHandler>();
        services.AddScoped<PhoneLoginService>();

        return services;
    }
}
