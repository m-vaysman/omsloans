using System.Data.Common;
using OmsLoan.Data.Postgres;

namespace OmsLoan.Api.Ops;

public static class OpsConnectionEndpoint
{
    private const int DefaultPostgresPort = 5432;

    // Npgsql: Host. SqlClient: Server / Data Source. Same field either way.
    private static readonly string[] HostKeys =
        ["host", "server", "data source", "datasource", "address", "addr", "network address"];

    // Host and port only. Password, user, and database name stay out of the payload.
    // DbConnectionStringBuilder rather than a split on ';': a quoted value may hold a
    // semicolon, and hand-splitting Password='a;Host=SECRET' puts half the password
    // in the endpoint.
    public static string? Describe(string? connectionString, string? provider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        DbConnectionStringBuilder parsed;

        try
        {
            parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            return null;
        }

        var host = HostKeys
            .Select(key => Value(parsed, key))
            .FirstOrDefault(value => value is not null);

        if (host is null)
        {
            return null;
        }

        var port = Value(parsed, "port");

        if (port is not null)
        {
            return $"{host}:{port}";
        }

        // Postgres: omitted Port means 5432. SQL Server: host only — do not invent 1433.
        return string.Equals(provider, DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase)
            ? $"{host}:{DefaultPostgresPort}"
            : host;
    }

    private static string? Value(DbConnectionStringBuilder parsed, string key) =>
        parsed.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;
}
