using Domain.Common;

namespace Domain.Network;

/// <summary>
/// The short codes hubs and zones are known by (MIR, GUL). They are printed on labels, typed into scanners and read
/// out over the phone, so they are kept to letters and digits and held in capitals.
/// </summary>
public static class Codes
{
    public const int MaxLength = 20;

    public static Result<string> Read(string? code, string errorCode, string message)
    {
        var trimmed = code.NullIfBlank()?.ToUpperInvariant();
        if (trimmed is null || trimmed.Length > MaxLength || !trimmed.All(char.IsAsciiLetterOrDigit))
        {
            return Error.Validation(errorCode, message);
        }

        return trimmed;
    }
}
