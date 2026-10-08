using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Browse;
using Domain.Parcels;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant's parcels as a spreadsheet: the stage, the days booked and a search chosen here (the parcel list's
/// "Export" button brings its own), every matching parcel up to <see cref="ParcelListHandler.MaxExport"/> in one file.
/// </summary>
public class ExportModel(ParcelListHandler parcels) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public ParcelTab Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Late { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetCsvAsync(CancellationToken cancellationToken)
    {
        var rows = await parcels.ExportAsync(
            new ParcelQuery { Tab = Tab, Search = Search, From = From, To = To, Late = Late },
            cancellationToken);
        var csv = new StringBuilder(
            "tracking_code,invoice,recipient,phone,address,area,status,problem,cod_amount,collected,delivery_charge,cod_charge," +
            "total_charge,weight_kg,booked,finished,due_by\r\n");
        foreach (var row in rows)
        {
            csv.Append(string.Join(",", new[]
            {
                row.TrackingCode, row.MerchantReference ?? "", row.RecipientName, row.RecipientPhone, row.RecipientAddress, row.Area,
                row.Status.DisplayName(), row.Issue?.DisplayName() ?? "", Number(row.CodAmount), row.CollectedAmount is { } got ? Number(got) : "",
                Number(row.DeliveryCharge), Number(row.CodCharge), Number(row.TotalCharge),
                (row.WeightGrams / 1000m).ToString("0.###", CultureInfo.InvariantCulture),
                row.Booked.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                row.Closed?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                row.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
            }.Select(Quote))).Append("\r\n");
        }

        var name = From is null && To is null
            ? "parcels.csv"
            : $"parcels-{From?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "start"}-{To?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "today"}.csv";

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", name);

        static string Number(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static string Quote(string value)
        {
            return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
        }
    }
}
