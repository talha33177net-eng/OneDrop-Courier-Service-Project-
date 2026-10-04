using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Browse;
using Domain.Parcels;

namespace Web.Pages.Merchant;

/// <summary>The merchant's parcels: tabs by stage, search, a date range, label printing for the ones ticked and a CSV export.</summary>
public class ParcelsModel(ParcelListHandler parcels) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public ParcelTab Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public ParcelList List { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        List = await parcels.ListAsync(Query(PageNumber, 25), cancellationToken);
    }

    /// <summary>The parcels of the current tab and filters as a CSV file, up to the list's largest page.</summary>
    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var list = await parcels.ListAsync(Query(1, ParcelListHandler.MaxPageSize), cancellationToken);
        var csv = new StringBuilder("tracking_code,invoice,recipient,phone,area,status,cod_amount,collected,delivery_charge,booked\r\n");
        foreach (var row in list.Page.Items)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                row.TrackingCode, row.MerchantReference ?? "", row.RecipientName, row.RecipientPhone, row.Area,
                row.Status.DisplayName(), row.CodAmount.ToString(CultureInfo.InvariantCulture),
                row.CollectedAmount?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.DeliveryCharge.ToString(CultureInfo.InvariantCulture), row.Booked.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            }.Select(Quote)));
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "parcels.csv");

        static string Quote(string value)
        {
            return value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
        }
    }

    private ParcelQuery Query(int page, int size)
    {
        return new ParcelQuery { Tab = Tab, Search = Search, From = From, To = To, Page = page, PageSize = size };
    }
}
