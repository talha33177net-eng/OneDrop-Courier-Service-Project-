using FluentValidation;
using Domain.Customers;
using Domain.Orders;

namespace Application.Pricing.GetQuote;

/// <summary>The same shape rules as Create Order for the fields a quote needs.</summary>
public class GetQuoteValidator : AbstractValidator<GetQuoteQuery>
{
    public GetQuoteValidator()
    {
        RuleFor(query => query.Phone)
            .Must(phone => PhoneNumber.Parse(phone).IsSuccess)
            .WithMessage("Enter a Bangladeshi mobile number such as 01712345678.");
        RuleFor(query => query.Line1).NotEmpty().MaximumLength(300);
        RuleFor(query => query.Line2).MaximumLength(300);
        RuleFor(query => query.Speed).IsInEnum();
        RuleFor(query => query.WeightGrams).InclusiveBetween(1, Order.MaxPackages * Order.MaxPackageWeightGrams);
        RuleFor(query => query)
            .Must(query => query.AreaId.HasValue || !string.IsNullOrWhiteSpace(query.Area))
            .OverridePropertyName(nameof(GetQuoteQuery.Area))
            .WithMessage("Give the area id or the area name from GET /api/v1/areas.");
    }
}
