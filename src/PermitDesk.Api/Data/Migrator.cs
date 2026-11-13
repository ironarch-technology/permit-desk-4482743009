using Npgsql;

namespace PermitDesk.Api.Data;

/// <summary>
/// Applies the numbered SQL files in db/migrations, once each, in order.
/// Run as its own step ("migrate") before the service starts.
/// </summary>
public static class Migrator
{
    public static async Task<int> RunAsync(string connectionString, string directory, ILogger log)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, null, "SELECT pg_advisory_lock(727001)");
        await ExecuteAsync(connection, null, @"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                filename text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now())");

        var applied = new HashSet<string>();
        await using (var command = new NpgsqlCommand("SELECT filename FROM schema_migrations", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) applied.Add(reader.GetString(0));
        }

        var count = 0;
        foreach (var path in Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (applied.Contains(name)) continue;

            await using var tx = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, tx, await File.ReadAllTextAsync(path));

            await using (var record = new NpgsqlCommand("INSERT INTO schema_migrations (filename) VALUES (@name)", connection, tx))
            {
                record.Parameters.AddWithValue("name", name);
                await record.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            log.LogInformation("Applied migration {Migration}", name);
            count++;
        }

        await ExecuteAsync(connection, null, "SELECT pg_advisory_unlock(727001)");
        return count;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        await command.ExecuteNonQueryAsync();
    }
}
