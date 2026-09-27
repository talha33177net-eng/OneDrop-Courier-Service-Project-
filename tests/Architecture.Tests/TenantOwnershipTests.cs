using Domain.Common;
using Domain.Platform;

namespace Architecture.Tests;

/// <summary>
/// Must-pass rule from the plan: every entity except the Platform module has a TenantId. An entity without one
/// would be invisible to the tenant filter and the save interceptor, so its rows would leak across tenants.
/// </summary>
public class TenantOwnershipTests
{
    private static readonly IReadOnlyList<Type> Entities = typeof(Entity).Assembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Entity).IsAssignableFrom(type))
        .ToList();

    [Fact]
    public void Every_entity_outside_Platform_is_tenant_owned()
    {
        var platformNamespace = typeof(Tenant).Namespace;
        var missing = Entities
            .Where(type => type.Namespace != platformNamespace && !typeof(ITenantOwned).IsAssignableFrom(type))
            .Select(type => type.FullName);

        Assert.Empty(missing);
    }

    [Fact]
    public void Platform_entities_are_not_tenant_owned()
    {
        Assert.False(typeof(ITenantOwned).IsAssignableFrom(typeof(Tenant)));
    }

    [Fact]
    public void Tenant_ownership_comes_from_the_base_class_so_business_code_cannot_set_it()
    {
        var handRolled = Entities
            .Where(type => typeof(ITenantOwned).IsAssignableFrom(type) && !typeof(TenantEntity).IsAssignableFrom(type))
            .Select(type => type.FullName);

        Assert.Empty(handRolled);
        Assert.Null(typeof(TenantEntity).GetProperty(nameof(TenantEntity.TenantId))!.GetSetMethod());
    }
}
