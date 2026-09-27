using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Application.Auth.PhoneLogin;
using Application.Customers;
using Application.Network.ListAreas;
using Application.Orders.CreateOrder;
using Application.Orders.GetOrder;

namespace Application;

public static class DependencyInjection
{
    /// <summary>Handlers are plain scoped classes, injected where they are used. No mediator.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        services.AddScoped<CustomerDirectory>();
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ListAreasHandler>();
        services.AddScoped<PhoneLoginService>();

        return services;
    }
}
