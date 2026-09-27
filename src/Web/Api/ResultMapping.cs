using Microsoft.AspNetCore.Mvc;
using Domain.Common;

namespace Web.Api;

/// <summary>Turns a business "no" into RFC 9457 problem details with the error code the caller can switch on.</summary>
public static class ResultMapping
{
    public static ActionResult ToProblem(this ControllerBase controller, Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = error.Fields is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(error.Fields.ToDictionary());
        problem.Status = status;
        problem.Title = error.Message;
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
