using Domain.Grouping;
using Domain.Orders;

namespace Web.Display;

/// <summary>
/// The words and colours the pages use for order and delivery statuses, so a shop, a customer and hub staff read the
/// same thing for the same status instead of the enum's name.
/// </summary>
public static class Statuses
{
    public static string Order(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Created => "Waiting for pickup",
            OrderStatus.PickedUp => "Picked up",
            OrderStatus.AtHub => "At the hub",
            OrderStatus.OutForDelivery => "Out for delivery",
            OrderStatus.Delivered => "Delivered",
            OrderStatus.Refused => "Refused",
            OrderStatus.ReturnedToMerchant => "Returned to the shop",
            _ => "Cancelled"
        };
    }

    /// <summary>The status pill's colour: waiting (amber), moving (blue), done (green), problem (red), quiet (grey).</summary>
    public static string OrderClass(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Created => "status status-waiting",
            OrderStatus.PickedUp or OrderStatus.AtHub or OrderStatus.OutForDelivery => "status status-moving",
            OrderStatus.Delivered => "status status-done",
            OrderStatus.Refused or OrderStatus.ReturnedToMerchant => "status status-problem",
            _ => "status status-quiet"
        };
    }

    public static string Delivery(DeliveryGroupStatus status)
    {
        return status switch
        {
            DeliveryGroupStatus.Open => "Waiting for more shops",
            DeliveryGroupStatus.Locked => "Closed",
            DeliveryGroupStatus.Dispatched => "Out for delivery",
            DeliveryGroupStatus.Delivered => "Delivered",
            _ => "Cancelled"
        };
    }
}
