using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The schema is owned by src/Database and EF only maps onto it, so the two can drift: a column added
/// to an entity but not to the table file fails at runtime, on the first query that touches it. This compares
/// every mapped column against the published database - existence, type and nullability.
/// </summary>
public class SchemaMatchesModelTests(WebAppFactory factory)
{
    [Fact]
    public async Task Every_mapped_column_exists_with_the_same_type_and_nullability()
    {
        WebAppFactory.RequireDatabase();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columns = await ReadColumnsAsync();
        var problems = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            var schema = entity.GetSchema() ?? "dbo";
            var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, schema);

            foreach (var property in entity.GetProperties())
            {
                var name = property.GetColumnName(store)!;
                if (!columns.TryGetValue($"{schema}.{table}.{name}", out var column))
                {
                    problems.Add($"{schema}.{table}.{name} is mapped by {entity.ClrType.Name} but missing from the database");
                    continue;
                }

                var expectedType = Normalise(property.GetColumnType(store));
                if (expectedType != column.Type)
                {
                    problems.Add($"{schema}.{table}.{name}: EF expects {expectedType}, the database has {column.Type}");
                }

                if (property.IsColumnNullable(store) != column.Nullable)
                {
                    problems.Add($"{schema}.{table}.{name}: EF nullable={property.IsColumnNullable(store)}, database nullable={column.Nullable}");
                }
            }
        }

        Assert.Empty(problems);
    }

    private static async Task<Dictionary<string, (string Type, bool Nullable)>> ReadColumnsAsync()
    {
        const string sql = """
            SELECT
                C.TABLE_SCHEMA + '.' + C.TABLE_NAME + '.' + C.COLUMN_NAME,
                C.DATA_TYPE,
                C.CHARACTER_MAXIMUM_LENGTH,
                C.NUMERIC_PRECISION,
                C.NUMERIC_SCALE,
                C.DATETIME_PRECISION,
                C.IS_NULLABLE
            FROM
                INFORMATION_SCHEMA.COLUMNS C
            """;

        var columns = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(WebAppFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var dataType = reader.GetString(1);
            var type = dataType switch
            {
                "nvarchar" or "nchar" or "varchar" or "char" or "binary" or "varbinary" =>
                    $"{dataType}({(reader.GetInt32(2) == -1 ? "max" : reader.GetInt32(2))})",
                "decimal" or "numeric" => $"decimal({reader.GetByte(3)},{reader.GetInt32(4)})",
                "datetime2" or "datetimeoffset" => $"{dataType}({reader.GetInt16(5)})",
                "timestamp" => "rowversion",
                _ => dataType
            };
            columns[reader.GetString(0)] = (type, reader.GetString(6) == "YES");
        }

        return columns;
    }

    /// <summary>EF writes "datetime2" or "datetimeoffset" for the default precision of 7.</summary>
    private static string Normalise(string? efType)
    {
        var type = (efType ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

        return type is "datetime2" or "datetimeoffset" ? $"{type}(7)" : type;
    }
}
