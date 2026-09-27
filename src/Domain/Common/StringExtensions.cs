namespace Domain.Common;

public static class StringExtensions
{
    public static string? NullIfBlank(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
