using OmsLoan.Api.Ops;
using static OmsLoan.Api.Tests.Ops.OpsTestConfiguration;

namespace OmsLoan.Api.Tests.Ops;

public class OpsRedactionTests
{
    private const string Password = "NEVER-SHOW";

    private const string ProviderKeyValue = "sk-never-show-this";

    private static Dictionary<string, string?> HostSettings() => new()
    {
        ["ConnectionStrings:OmsLoan"] =
            $"Host=db01;Port=6432;Database=omsloan;Username=omsloan;Password={Password}",
        ["Database:Provider"] = "Postgres",
        ["CLAUDE_API_KEY"] = ProviderKeyValue,
    };

    [Fact]
    public async Task ThePayloadNeverContainsTheConnectionString()
    {
        var json = Serialize(await Build(Configuration(HostSettings())));

        Assert.DoesNotContain(Password, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheDatabaseEndpointIsHostAndPortOnly()
    {
        var status = await Build(Configuration(HostSettings()));

        Assert.Equal("db01:6432", status.Database.Endpoint);
    }

    [Fact]
    public async Task SecretsAppearAsPresentOrAbsentOnly()
    {
        var status = await Build(Configuration(HostSettings()));
        var json = Serialize(status);

        var claude = status.Secrets.Single(secret => secret.Name == "CLAUDE_API_KEY");
        var groq = status.Secrets.Single(secret => secret.Name == "GROQ_API_KEY");

        Assert.True(claude.Present);
        Assert.False(groq.Present);
        Assert.DoesNotContain(ProviderKeyValue, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheConnectionStringIsReportedAsPresentWithoutItsValue()
    {
        var status = await Build(Configuration(HostSettings()));

        Assert.True(status.Secrets.Single(secret => secret.Name == "ConnectionStrings__OmsLoan").Present);
    }

    [Fact]
    public void ALogLineKeepsItsFirstLineOnly()
    {
        var message = OpsLogText.Summarize(
            "Npgsql.NpgsqlException: connection refused\r\n   at Npgsql.Internal.NpgsqlConnector.Connect()\r\n   at Npgsql.NpgsqlConnection.Open()",
            300);

        Assert.Equal("Npgsql.NpgsqlException: connection refused", message);
    }

    [Fact]
    public void ALongMessageIsTruncatedToTheConfiguredLength()
    {
        var message = OpsLogText.Summarize(new string('x', 500), 300);

        Assert.Equal(new string('x', 300) + OpsLogText.Ellipsis, message);
    }

    [Fact]
    public void AnEmptyMessageStaysEmpty()
    {
        Assert.Equal(string.Empty, OpsLogText.Summarize(null, 300));
        Assert.Equal(string.Empty, OpsLogText.Summarize("   \r\n  ", 300));
    }

    [Fact]
    public void ASectionNeverReturnsMoreThanTheEntryCap()
    {
        var entries = Enumerable
            .Range(0, 200)
            .Select(index => new OpsLogEntry(Now, "Information", $"line {index}\r\nstack frame"))
            .ToList();

        var trimmed = OpsLogText.Trim(entries, 50, 300);

        Assert.Equal(50, trimmed.Count);
        Assert.All(trimmed, entry => Assert.DoesNotContain("stack frame", entry.Message, StringComparison.Ordinal));
    }
}
