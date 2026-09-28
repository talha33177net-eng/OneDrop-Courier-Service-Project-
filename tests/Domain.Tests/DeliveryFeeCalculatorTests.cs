using Domain.Orders;
using Domain.Pricing;

namespace Domain.Tests;

public class DeliveryFeeCalculatorTests
{
    // The launch tenants' prices (seed 001, Dhaka's fast fee from 003). The calculator holds none of its own.
    private static readonly FeeSchedule Dhaka = new(60, 25, 70);
    private static readonly FeeSchedule Chattogram = new(70, 30, 80);

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 85)]
    [InlineData(3, 110)]
    [InlineData(4, 135)]
    public void A_group_costs_the_base_fee_plus_the_extra_fee_for_every_other_shop(int shops, decimal fee)
    {
        var orders = Enumerable.Range(1, shops).Select(merchant => Waiting(merchant));

        Assert.Equal(fee, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
    }

    [Theory]
    [InlineData(1, 70)]
    [InlineData(3, 130)]
    public void Another_tenant_charges_its_own_prices(int shops, decimal fee)
    {
        var orders = Enumerable.Range(1, shops).Select(merchant => Waiting(merchant));

        Assert.Equal(fee, new DeliveryFeeCalculator(Chattogram).GroupFee(orders));
    }

    [Fact]
    public void Several_orders_from_one_shop_count_once()
    {
        FeeLine[] orders = [Waiting(1), Waiting(1), Waiting(2), Waiting(2), Waiting(2)];

        Assert.Equal(85, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refused)]
    [InlineData(OrderStatus.ReturnedToMerchant)]
    public void A_shop_whose_order_is_not_delivered_is_not_charged(OrderStatus status)
    {
        FeeLine[] orders = [Waiting(1), Waiting(2), Waiting(3) with { Status = status }];

        Assert.Equal(85, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
    }

    [Fact]
    public void A_shop_with_one_refused_and_one_delivered_order_is_still_charged()
    {
        FeeLine[] orders =
        [
            Waiting(1) with { Status = OrderStatus.Delivered },
            Waiting(2) with { Status = OrderStatus.Delivered },
            Waiting(2) with { Status = OrderStatus.Refused }
        ];

        Assert.Equal(85, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
    }

    [Fact]
    public void Nothing_delivered_costs_nothing()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);

        Assert.Equal(0, calculator.GroupFee([]));
        Assert.Equal(0, calculator.GroupFee([Waiting(1) with { Status = OrderStatus.Refused }]));
    }

    [Fact]
    public void Deliver_fast_costs_the_tenants_fast_fee()
    {
        FeeLine[] fast = [Waiting(1) with { Speed = DeliverySpeed.Fast }];

        Assert.Equal(70, new DeliveryFeeCalculator(Dhaka).GroupFee(fast));
        Assert.Equal(80, new DeliveryFeeCalculator(Chattogram).GroupFee(fast));
    }

    [Fact]
    public void Each_order_adds_what_its_shop_costs_the_group()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);

        Assert.Equal(60, calculator.AddedFee([], Waiting(1)));
        Assert.Equal(25, calculator.AddedFee([Waiting(1)], Waiting(2)));
        Assert.Equal(25, calculator.AddedFee([Waiting(1), Waiting(2)], Waiting(3)));
        Assert.Equal(0, calculator.AddedFee([Waiting(1), Waiting(2)], Waiting(2)));
    }

    [Fact]
    public void An_order_joining_after_the_only_other_shop_was_cancelled_opens_the_fee_again()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);

        Assert.Equal(60, calculator.AddedFee([Waiting(1) with { Status = OrderStatus.Cancelled }], Waiting(2)));
    }

    [Fact]
    public void A_fast_order_alone_adds_the_fast_fee_and_a_Dont_hold_order_alone_adds_the_base_fee()
    {
        var calculator = new DeliveryFeeCalculator(Chattogram);

        Assert.Equal(80, calculator.AddedFee([], Waiting(1) with { Speed = DeliverySpeed.Fast }));
        Assert.Equal(70, calculator.AddedFee([], Waiting(1)));
    }

    [Theory]
    [InlineData(2, 35)]
    [InlineData(3, 70)]
    [InlineData(4, 105)]
    public void Several_shops_save_what_separate_one_shop_deliveries_would_cost_more(int shops, decimal savings)
    {
        var orders = Enumerable.Range(1, shops).Select(merchant => Waiting(merchant)).ToList();

        Assert.Equal(savings, new DeliveryFeeCalculator(Dhaka).Savings(orders));
    }

    [Fact]
    public void Savings_use_the_tenants_own_prices()
    {
        FeeLine[] orders = [Waiting(1), Waiting(2), Waiting(3)];

        // Three separate ৳70 deliveries against ৳70 + 2 × ৳30
        Assert.Equal(80, new DeliveryFeeCalculator(Chattogram).Savings(orders));
    }

    [Fact]
    public void One_shop_saves_nothing_even_when_its_fast_fee_is_below_the_base_fee()
    {
        var calculator = new DeliveryFeeCalculator(new FeeSchedule(60, 25, 50));

        Assert.Equal(0, calculator.Savings([Waiting(1), Waiting(1)]));
        Assert.Equal(0, calculator.Savings([Waiting(1) with { Speed = DeliverySpeed.Fast }]));
        Assert.Equal(0, calculator.Savings([]));
    }

    [Fact]
    public void A_shop_whose_order_is_not_delivered_saves_nothing()
    {
        FeeLine[] orders = [Waiting(1), Waiting(2), Waiting(3) with { Status = OrderStatus.Refused }];

        Assert.Equal(35, new DeliveryFeeCalculator(Dhaka).Savings(orders));
    }

    private static FeeLine Waiting(long merchantId)
    {
        return new FeeLine(merchantId, DeliverySpeed.Combine, OrderStatus.Created);
    }
}
