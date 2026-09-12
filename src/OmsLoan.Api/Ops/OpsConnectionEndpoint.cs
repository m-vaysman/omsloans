using OmsLoan.Data.Postgres;

namespace OmsLoan.Api.Ops;

public static class OpsConnectionEndpoint
{
    private const int DefaultPostgresPort = 5432;

    // Host and port only. Password, user, and database name stay out of the payload.
    // Split is on ';' and does not honor quoting.
    public static string? Describe(string? connectionString, string? provider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        string? host = null;
        string? port = null;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            var value = part[(separator + 1)..].Trim();

            if (value.Length == 0)
            {
                continue;
            }

            // Npgsql: Host. SqlClient: Server / Data Source. Same field either way.
            switch (key)
            {
                case "host":
                case "server":
                case "datasource":
                case "address":
                case "addr":
                case "networkaddress":
                    host = value;
                    break;
                case "port":
                    port = value;
                    break;
            }
        }

        if (host is null)
        {
            return null;
        }

        if (port is not null)
        {
            return $"{host}:{port}";
        }

        // Postgres: omitted Port means 5432. SQL Server: host only — do not invent 1433.
        return string.Equals(provider, DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase)
            ? $"{host}:{DefaultPostgresPort}"
            : host;
    }
}
