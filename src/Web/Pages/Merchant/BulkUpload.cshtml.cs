using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.ListAreas;
using Application.Parcels.BulkImport;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant books many parcels from a CSV file (exported from a spreadsheet or a shop). Every row is checked first;
/// nothing is booked until all rows are good, and the page lists the rows to fix.
/// </summary>
public class BulkUploadModel(BulkImportHandler handler, ListAreasHandler areas) : PageModel
{
    public const long MaxBytes = 1024 * 1024;

    public ImportResult? Result { get; private set; }

    public string? Problem { get; private set; }

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
        if (file is null || file.Length == 0)
        {
            Problem = "Choose a CSV file to upload.";
            return Page();
        }

        if (file.Length > MaxBytes)
        {
            Problem = "The file is larger than 1 MB. Split it into smaller files.";
            return Page();
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var imported = await handler.ImportAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        if (imported.IsFailure)
        {
            Problem = imported.Error!.Message;
            return Page();
        }

        Result = imported.Value;

        return Page();
    }

    public async Task<IActionResult> OnGetTemplateAsync(CancellationToken cancellationToken)
    {
        var area = (await areas.HandleAsync(cancellationToken)).FirstOrDefault()?.Name ?? "Mirpur 10";

        return File(Encoding.UTF8.GetBytes(BulkImportHandler.Template(area)), "text/csv", "parcels-template.csv");
    }
}
