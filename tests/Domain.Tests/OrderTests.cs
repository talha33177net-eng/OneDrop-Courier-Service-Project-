using Domain.Common;
using Domain.Orders;

namespace Domain.Tests;

public class OrderTests
{
    private static NewOrder Spec(params NewPackage[] packages)
    {
        return new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: " Rahim ",
            CodAmount: 800,
            DeclaredValue: 800,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: packages);
    }

    [Fact]
    public void A_new_order_is_created_with_numbered_packages_and_a_first_status()
    {
        var order = Order.Create(Spec(new NewPackage("T-shirt", 400), new NewPackage("Cap", 150))).Value;

        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal("Rahim", order.RecipientName);
        Assert.Equal([1, 2], order.Packages.Select(p => p.Sequence));
        Assert.All(order.Packages, p => Assert.Equal(7, p.MerchantId));
        Assert.Equal(550, order.TotalWeightGrams);
        Assert.Single(order.History);
        Assert.Equal(7, order.History[0].MerchantId);
    }

    [Fact]
    public void An_order_needs_a_package()
    {
        Assert.Equal("order.packages.required", Order.Create(Spec()).Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Order.MaxPackageWeightGrams + 1)]
    public void A_package_weight_must_be_sensible(int grams)
    {
        Assert.Equal("order.packages.weight", Order.Create(Spec(new NewPackage("Box", grams))).Error!.Code);
    }

    [Fact]
    public void Amounts_cannot_be_negative()
    {
        var spec = Spec(new NewPackage("Box", 100)) with { CodAmount = -1 };

        Assert.Equal("order.amounts.negative", Order.Create(spec).Error!.Code);
    }

    [Fact]
    public void Status_follows_the_delivery_path_and_is_recorded()
    {
        var order = Order.Create(Spec(new NewPackage("Box", 100))).Value;

        Assert.True(order.MoveTo(OrderStatus.PickedUp).IsSuccess);
        Assert.True(order.MoveTo(OrderStatus.AtHub).IsSuccess);
        Assert.True(order.MoveTo(OrderStatus.OutForDelivery).IsSuccess);
        Assert.True(order.MoveTo(OrderStatus.Delivered).IsSuccess);

        Assert.Equal(
            [OrderStatus.Created, OrderStatus.PickedUp, OrderStatus.AtHub, OrderStatus.OutForDelivery, OrderStatus.Delivered],
            order.History.Select(h => h.Status));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.ReturnedToMerchant)]
    public void A_new_order_cannot_skip_ahead(OrderStatus target)
    {
        var order = Order.Create(Spec(new NewPackage("Box", 100))).Value;

        var moved = order.MoveTo(target);

        Assert.Equal(ErrorType.Conflict, moved.Error!.Type);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void A_delivered_order_is_final()
    {
        var order = Order.Create(Spec(new NewPackage("Box", 100))).Value;
        order.MoveTo(OrderStatus.PickedUp);
        order.MoveTo(OrderStatus.AtHub);
        order.MoveTo(OrderStatus.OutForDelivery);
        order.MoveTo(OrderStatus.Delivered);

        Assert.All(Enum.GetValues<OrderStatus>(), status => Assert.False(order.CanMoveTo(status)));
    }
}
