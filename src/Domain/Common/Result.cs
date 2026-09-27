namespace Domain.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden
}

/// <summary>A business "no". Codes are stable and safe to return to API callers.</summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Validation)
{
    /// <summary>Per-field messages for validation failures, keyed by the request property path.</summary>
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }

    public static Error Validation(string code, string message)
    {
        return new Error(code, message);
    }

    public static Error NotFound(string code, string message)
    {
        return new Error(code, message, ErrorType.NotFound);
    }

    public static Error Conflict(string code, string message)
    {
        return new Error(code, message, ErrorType.Conflict);
    }

    public static Error Forbidden(string code, string message)
    {
        return new Error(code, message, ErrorType.Forbidden);
    }
}

/// <summary>
/// Result pattern: a use case returns success or an <see cref="Error"/> instead of throwing for expected
/// business outcomes. Exceptions are kept for bugs and infrastructure failures.
/// </summary>
public class Result
{
    protected Result(Error? error)
    {
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success()
    {
        return new Result(null);
    }

    public static Result Failure(Error error)
    {
        return new Result(error);
    }

    public static Result<T> Success<T>(T value)
    {
        return Result<T>.Success(value);
    }

    public static implicit operator Result(Error error)
    {
        return Failure(error);
    }
}

public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(T? value, Error? error)
        : base(error)
    {
        this.value = value;
    }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    public static Result<T> Success(T value)
    {
        return new Result<T>(value, null);
    }

    public static new Result<T> Failure(Error error)
    {
        return new Result<T>(default, error);
    }

    public static implicit operator Result<T>(T value)
    {
        return Success(value);
    }

    public static implicit operator Result<T>(Error error)
    {
        return Failure(error);
    }
}
