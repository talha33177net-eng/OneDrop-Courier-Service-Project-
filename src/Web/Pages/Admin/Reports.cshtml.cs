using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Reports;

namespace Web.Pages.Admin;

/// <summary>
/// The courier's own reports over a period: the cash and charges day by day, each rider's deliveries and cash, and
/// the returns each merchant had. Each table downloads as CSV for a spreadsheet.
/// </summary>
public class ReportsModel(ReportsHandler reports) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public CourierReport Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await reports.ForAsync(From, To, cancellationToken);
        From = Report.From;
        To = Report.To;
    }

    /// <summary>One of the three tables as a CSV file: "days", "riders" or "returns".</summary>
    public async Task<IActionResult> OnGetExportAsync(string table, CancellationToken cancellationToken)
    {
        var report = await reports.ForAsync(From, To, cancellationToken);
        var csv = new StringBuilder();
        switch (table)
        {
            case "riders":
                csv.AppendLine("rider,hub,runs,delivered,held,refused,collected,handed_in,short,open_runs");
                foreach (var row in report.Riders)
                {
                    csv.AppendLine(Line(
                        row.Rider, row.Hub, row.Runs, row.Delivered, row.Held, row.Refused,
                        row.Collected, row.Received, row.Short, row.OpenRuns));
                }

                break;

            case "returns":
                csv.AppendLine("merchant,delivered,returned,return_rate,return_charges");
                foreach (var row in report.Returns)
                {
                    csv.AppendLine(Line(
                        row.Merchant, row.Delivered, row.Returned,
                        Math.Round(row.ReturnRate * 100, 1), row.ReturnCharges));
                }

                break;

            default:
                csv.AppendLine("day,delivered,returned,cod_collected,delivery_charges,cod_charges,return_charges,earned,owed_to_merchants");
                foreach (var row in report.Days)
                {
                    csv.AppendLine(Line(
                        row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Delivered, row.Returned,
                        row.Cod, row.DeliveryCharges, row.CodCharges, row.ReturnCharges, row.Earned, row.OwedToMerchants));
                }

                break;
        }

        var name = $"{(table is "riders" or "returns" ? table : "days")}-{report.From:yyyy-MM-dd}-to-{report.To:yyyy-MM-dd}.csv";

        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv",
            name);
    }

    private static string Line(params object[] values)
    {
        return string.Join(",", values.Select(value =>
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            return text.Contains(',') || text.Contains('"')
                ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : text;
        }));
    }
}
