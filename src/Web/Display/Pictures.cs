namespace Web.Display;

public static class Pictures
{
    /// <summary>
    /// Where a business's picture is served, or null when it has none. The change time is in the address so a new
    /// picture is fetched at once while an unchanged one stays in the browser's cache.
    /// </summary>
    public static string? Url(long merchantId, DateTime? changed)
    {
        return changed is { } on ? $"/Merchant/Picture/{merchantId}?v={on.Ticks}" : null;
    }
}
