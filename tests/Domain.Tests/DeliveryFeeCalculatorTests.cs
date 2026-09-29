using Domain.Orders;
using Domain.Pricing;

namespace Domain.Tests;

public class DeliveryFeeCalculatorTests
{
    // The launch tenants' prices (seed 001, fast fee from 003, weight from 004). The calculator holds none of its own.
    private static readonly FeeSchedule Dhaka = new(60, 25, 70, 2000, 15);
    private static readonly FeeSchedule Chattogram = new(70, 30, 80, 2000, 20);

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
        var calculator = new DeliveryFeeCalculator(new FeeSchedule(60, 25, 50, 2000, 15));

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

    [Theory]
    [InlineData(2000, 60, 70)]
    [InlineData(2001, 75, 90)] // one gram over starts a kilogram
    [InlineData(3000, 75, 90)] // a 3 kg shop pays one extra kg: ৳15 in Dhaka, ৳20 in Chattogram
    [InlineData(3001, 90, 110)]
    public void Each_started_kg_above_the_allowance_costs_the_tenants_extra_kg_fee(
        int grams,
        decimal dhaka,
        decimal chattogram)
    {
        FeeLine[] orders = [Waiting(1) with { WeightGrams = grams }];

        Assert.Equal(dhaka, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
        Assert.Equal(chattogram, new DeliveryFeeCalculator(Chattogram).GroupFee(orders));
    }

    [Fact]
    public void The_allowance_is_per_shop_and_counts_all_its_orders()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);

        // Two shops of 1.5 kg each: both inside their allowance
        FeeLine[] twoShops = [Waiting(1) with { WeightGrams = 1500 }, Waiting(2) with { WeightGrams = 1500 }];
        Assert.Equal(85, calculator.GroupFee(twoShops));

        // One shop's two orders of 1.5 kg: 3 kg, one kg over
        FeeLine[] oneShop = [Waiting(1) with { WeightGrams = 1500 }, Waiting(1) with { WeightGrams = 1500 }];
        Assert.Equal(75, calculator.GroupFee(oneShop));
    }

    [Fact]
    public void An_order_taking_its_shop_past_the_allowance_adds_the_kg_fee()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);
        FeeLine[] group = [Waiting(1) with { WeightGrams = 1500 }, Waiting(2)];

        Assert.Equal(15, calculator.AddedFee(group, Waiting(1) with { WeightGrams = 1500 }));
    }

    [Fact]
    public void A_parcel_not_delivered_is_not_weighed()
    {
        FeeLine[] orders = [Waiting(1), Waiting(1) with { WeightGrams = 5000, Status = OrderStatus.Refused }];

        Assert.Equal(60, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
    }

    [Fact]
    public void Weight_does_not_eat_into_the_saving()
    {
        FeeLine[] orders = [Waiting(1) with { WeightGrams = 3000 }, Waiting(2), Waiting(3)];

        // ৳125 with one kg over; three separate deliveries would also have charged that kg
        Assert.Equal(125, new DeliveryFeeCalculator(Dhaka).GroupFee(orders));
        Assert.Equal(70, new DeliveryFeeCalculator(Dhaka).Savings(orders));
    }

    [Fact]
    public void A_delivery_brought_forward_by_Ship_now_costs_the_fast_difference_on_its_first_shop()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);
        FeeLine[] orders = [Waiting(1), Waiting(2)];

        Assert.Equal(10, calculator.ShipNowFee);
        Assert.Equal(85, calculator.GroupFee(orders));
        Assert.Equal(95, calculator.GroupFee(orders, shippedNow: true));
        Assert.Equal(25, calculator.AddedFee(orders, Waiting(3), shippedNow: true));
        Assert.Equal(10, new DeliveryFeeCalculator(Chattogram).ShipNowFee);
    }

    [Fact]
    public void Ship_now_is_never_cheaper_than_waiting()
    {
        var calculator = new DeliveryFeeCalculator(new FeeSchedule(60, 25, 50, 2000, 15));

        Assert.Equal(0, calculator.ShipNowFee);
        Assert.Equal(60, calculator.GroupFee([Waiting(1)], shippedNow: true));
    }

    [Fact]
    public void Shops_joining_a_fast_delivery_add_the_extra_shop_fee()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);
        var fast = Waiting(1) with { Speed = DeliverySpeed.Fast };

        Assert.Equal(25, calculator.AddedFee([fast], Waiting(2)));
        Assert.Equal(95, calculator.GroupFee([fast, Waiting(2)]));

        // A fast order joining a Don't hold delivery (base fee) makes it fast and adds a shop
        Assert.Equal(35, calculator.AddedFee([Waiting(2)], fast));
    }

    private static FeeLine Waiting(long merchantId)
    {
        return new FeeLine(merchantId, DeliverySpeed.Combine, OrderStatus.Created, WeightGrams: 500);
    }
}
