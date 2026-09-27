using FluentValidation;
using Domain.Customers;
using Domain.Orders;

namespace Application.Orders.CreateOrder;

/// <summary>Shape checks. Rules that need the database (area exists, pickup point is ours) live in the handler.</summary>
public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public const decimal MaxAmount = 1_000_000m;

    public CreateOrderValidator()
    {
        RuleFor(command => command.ExternalReference).MaximumLength(100);
        RuleFor(command => command.IdempotencyKey).MaximumLength(100);
        RuleFor(command => command.Note).MaximumLength(500);
        RuleFor(command => command.Speed).IsInEnum();
        RuleFor(command => command.CodAmount).InclusiveBetween(0, MaxAmount);
        RuleFor(command => command.DeclaredValue).InclusiveBetween(0, MaxAmount);

        RuleFor(command => command.Customer).NotNull().WithMessage("Customer is required.");
        RuleFor(command => command.Customer!.Phone)
            .Must(phone => PhoneNumber.Parse(phone).IsSuccess)
            .WithMessage("Enter a Bangladeshi mobile number such as 01712345678.")
            .When(command => command.Customer is not null);
        RuleFor(command => command.Customer!.Name)
            .NotEmpty()
            .MaximumLength(200)
            .When(command => command.Customer is not null);

        RuleFor(command => command.Address).NotNull().WithMessage("Address is required.");
        When(command => command.Address is not null, () =>
        {
            RuleFor(command => command.Address!.Line1).NotEmpty().MaximumLength(300);
            RuleFor(command => command.Address!.Line2).MaximumLength(300);
            RuleFor(command => command.Address!.Landmark).MaximumLength(300);
            RuleFor(command => command.Address!)
                .Must(address => address.AreaId.HasValue || !string.IsNullOrWhiteSpace(address.Area))
                .OverridePropertyName("Address.Area")
                .WithMessage("Give the area id or the area name from GET /api/v1/areas.");
        });

        RuleFor(command => command.Packages)
            .NotEmpty()
            .WithMessage("An order needs at least one package.")
            .Must(packages => packages.Count <= Order.MaxPackages)
            .WithMessage($"An order can have at most {Order.MaxPackages} packages.");
        RuleForEach(command => command.Packages).ChildRules(package =>
        {
            package.RuleFor(p => p.Description).NotEmpty().MaximumLength(200);
            package.RuleFor(p => p.WeightGrams).InclusiveBetween(1, Order.MaxPackageWeightGrams);
        });
    }
}
