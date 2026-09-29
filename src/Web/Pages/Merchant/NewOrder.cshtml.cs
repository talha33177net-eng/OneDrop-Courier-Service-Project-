using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Network.ListAreas;
using Application.Orders.CreateOrder;
using Domain.Customers;
using Domain.Orders;

namespace Web.Pages.Merchant;

/// <summary>
/// The shop's order form, for sellers without a website (Facebook shops) and for trying the system by hand. It sends
/// the same command as <c>POST /api/v1/orders</c> through the same handler, so the fee, the grouping and the customer's
/// confirmation are exactly the API's. Each form carries its own idempotency key, so a double press saves one order.
/// </summary>
public class NewOrderModel(CreateOrderHandler handler, ListAreasHandler areas, ITenantContext tenantContext) : PageModel
{
    [BindProperty]
    public OrderForm Form { get; set; } = new();

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public TenantInfo Tenant => tenantContext.Tenant!;

    public IReadOnlyList<string> Problems { get; private set; } = [];

    [TempData]
    public string? CreatedNumber { get; set; }

    [TempData]
    public string? CreatedMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
        Form.Key = Guid.NewGuid().ToString("N");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var weight = (int)Math.Round(Form.WeightKg * 1000);
        var command = new CreateOrderCommand
        {
            ExternalReference = Blank(Form.Reference),
            Customer = new CustomerInput(Form.Name, Form.Phone),
            Address = new AddressInput(Form.AreaId, null, Form.Line1, Blank(Form.Line2), Blank(Form.Landmark)),
            Packages = [.. Enumerable.Range(0, Math.Clamp(Form.Parcels, 0, 20)).Select(_ => new PackageInput(Form.Description, weight))],
            CodAmount = Form.Cod,
            Speed = Form.Fast ? DeliverySpeed.Fast : DeliverySpeed.Combine,
            DoNotHold = Form.DoNotHold,
            FeeInAdvance = Form.FeeInAdvance,
            Note = Blank(Form.Note),
            IdempotencyKey = Form.Key
        };

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            var error = result.Error!;
            Problems = error.Fields is { Count: > 0 } fields
                ? [.. fields.SelectMany(field => field.Value.Select(message => $"{FieldName(field.Key)}: {message}"))]
                : [error.Message];
            Areas = await areas.HandleAsync(cancellationToken);

            return Page();
        }

        var order = result.Value;
        CreatedNumber = order.Number;
        CreatedMessage =
            $"For {Form.Name}, {order.Area}. It adds ৳{order.Fee:N0} to the customer's delivery fee, paid at the door. " +
            order.WaitsFor switch
            {
                CustomerStep.Confirm => "The customer is new: they get an SMS to confirm the order with one tap.",
                CustomerStep.PayInAdvance =>
                    "The customer pays the delivery fee in advance first: keep the parcel until the order says it is paid.",
                _ => "Print its label and hand it to the collector at the pickup."
            };

        return RedirectToPage();
    }

    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FieldName(string field)
    {
        return field switch
        {
            "Customer" or "Customer.Name" => "Customer name",
            "Customer.Phone" => "Mobile number",
            "Address" or "Address.Line1" => "Address",
            "Address.Line2" => "Address line 2",
            "Address.Landmark" => "Landmark",
            "CodAmount" => "Cash on delivery",
            "ExternalReference" => "Your order number",
            _ when field.StartsWith("Packages") => "Parcels",
            _ => field
        };
    }

    public sealed class OrderForm
    {
        public string? Name { get; set; }

        public string? Phone { get; set; }

        public long? AreaId { get; set; }

        public string? Line1 { get; set; }

        public string? Line2 { get; set; }

        public string? Landmark { get; set; }

        public int Parcels { get; set; } = 1;

        public string Description { get; set; } = "Parcel";

        public decimal WeightKg { get; set; } = 1;

        public decimal Cod { get; set; }

        public bool Fast { get; set; }

        public bool DoNotHold { get; set; }

        public bool FeeInAdvance { get; set; }

        public string? Reference { get; set; }

        public string? Note { get; set; }

        public string Key { get; set; } = "";
    }
}
