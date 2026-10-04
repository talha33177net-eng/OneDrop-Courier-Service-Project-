using System.Globalization;
using System.Text;
using Application.Abstractions;
using Application.Parcels.CreateParcel;
using Domain.Common;
using Domain.Parcels;

namespace Application.Parcels.BulkImport;

/// <summary>One row of the upload that could not be booked, by its line number in the file.</summary>
public sealed record ImportProblem(int Line, string Message);

/// <summary>The parcels booked, or the rows to fix: nothing is booked while any row has a problem.</summary>
public sealed record ImportResult(IReadOnlyList<CreateParcelResult> Booked, IReadOnlyList<ImportProblem> Problems);

/// <summary>
/// Books many parcels from a CSV file, as merchants export them from their shop or a spreadsheet. Every row is checked
/// first; only when all are good are they saved, in one go, so a file is never half booked. Columns, with a header row:
/// <c>invoice, recipient_name, recipient_phone, recipient_address, area, cod_amount, weight_kg, item_description, note</c>.
/// </summary>
public class BulkImportHandler(IAppDbContext db, CreateParcelHandler parcels)
{
    public const int MaxRows = 500;

    public static readonly string[] Columns =
    [
        "invoice", "recipient_name", "recipient_phone", "recipient_address", "area", "cod_amount", "weight_kg",
        "item_description", "note"
    ];

    public async Task<Result<ImportResult>> ImportAsync(string csv, CancellationToken cancellationToken = default)
    {
        var lines = ReadRows(csv);
        if (lines.Count == 0)
        {
            return Error.Validation("import.empty", "The file is empty. Download the template and fill in one parcel per row.");
        }

        var header = lines[0].Fields.Select(field => field.Trim().ToLowerInvariant()).ToList();
        var missing = Columns.Take(7).Where(column => !header.Contains(column)).ToList();
        if (missing.Count > 0)
        {
            return Error.Validation(
                "import.columns",
                $"The header row is missing {string.Join(", ", missing)}. Use the template's columns.");
        }

        var rows = lines.Skip(1).Where(row => row.Fields.Any(field => field.Trim().Length > 0)).ToList();
        if (rows.Count == 0)
        {
            return Error.Validation("import.noRows", "The file has a header but no parcels.");
        }

        if (rows.Count > MaxRows)
        {
            return Error.Validation("import.tooMany", $"Upload at most {MaxRows} parcels at a time.");
        }

        var problems = new List<ImportProblem>();
        var prepared = new List<(Parcel Parcel, string Area, string Hub)>();
        foreach (var row in rows)
        {
            string Field(string column)
            {
                var index = header.IndexOf(column);

                return index >= 0 && index < row.Fields.Count ? row.Fields[index].Trim() : "";
            }

            if (!TryAmount(Field("cod_amount"), out var cod) || !TryAmount(Field("weight_kg"), out var weight))
            {
                problems.Add(new ImportProblem(row.Line, "cod_amount and weight_kg must be numbers, such as 1250 and 0.5."));
                continue;
            }

            var command = new CreateParcelCommand
            {
                MerchantReference = Field("invoice").NullIfBlank(),
                RecipientName = Field("recipient_name"),
                RecipientPhone = Field("recipient_phone"),
                RecipientAddress = Field("recipient_address"),
                Area = Field("area"),
                CodAmount = cod,
                WeightKg = weight,
                ItemDescription = Field("item_description").NullIfBlank(),
                Note = Field("note").NullIfBlank()
            };
            var parcel = await parcels.PrepareAsync(command, null, cancellationToken);
            if (parcel.IsSuccess)
            {
                prepared.Add(parcel.Value);
            }
            else
            {
                var error = parcel.Error!;
                var detail = error.Fields is { Count: > 0 } fields
                    ? string.Join(" ", fields.Values.SelectMany(messages => messages))
                    : error.Message;
                problems.Add(new ImportProblem(row.Line, detail));
            }
        }

        if (problems.Count > 0)
        {
            return new ImportResult([], problems);
        }

        db.Parcels.AddRange(prepared.Select(p => p.Parcel));
        await db.SaveChangesAsync(cancellationToken);

        return new ImportResult([.. prepared.Select(p => CreateParcelHandler.Describe(p.Parcel, p.Area, p.Hub))], []);
    }

    /// <summary>The template a merchant downloads: the header and one example row.</summary>
    public static string Template(string exampleArea)
    {
        return string.Join(",", Columns) + "\r\n" +
            $"INV-1001,Rahim Uddin,01712345678,\"House 12, Road 5\",{exampleArea},1250,0.5,T-shirt,Call before coming\r\n";
    }

    private static bool TryAmount(string text, out decimal value)
    {
        return decimal.TryParse(
            text.Length == 0 ? "0" : text.Replace("৳", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    /// <summary>Splits CSV into rows of fields: commas, quoted fields with commas, doubled quotes and line breaks inside quotes.</summary>
    private static List<(int Line, List<string> Fields)> ReadRows(string csv)
    {
        var rows = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var rowLine = 1;
        var text = csv.TrimStart('﻿');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add((rowLine, fields));
                    fields = [];
                    line++;
                    rowLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add((rowLine, fields));
        }

        return rows;
    }
}
