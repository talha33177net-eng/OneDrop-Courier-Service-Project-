using FluentValidation.Results;
using Domain.Common;

namespace Application.Common;

public static class ValidationExtensions
{
    /// <summary>Turns FluentValidation failures into one validation <see cref="Error"/> with per-field detail.</summary>
    public static Error ToError(this ValidationResult validation)
    {
        var fields = validation.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        return Error.Validation("request.invalid", "The request has invalid fields.") with { Fields = fields };
    }
}
