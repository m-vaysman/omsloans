using Microsoft.Extensions.Configuration;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// The flat environment variables are a contract with machines that are already configured,
/// not a naming choice this repository is free to revisit. These tests exist to make a
/// rename fail here rather than silently at three in the morning on a host where every
/// secret is set and the Worker reports none of them.
/// </summary>
/// <remarks>
/// Nothing here reads or writes the real process environment. The mapping takes a lookup
/// function precisely so the suite stays hermetic: a test that depended on a machine
/// variable would pass on the developer's box and fail in CI, or worse, the reverse.
/// </remarks>
public class FlatEnvironmentSecretsTests
{
    private static IConfigurationRoot Build(
        IDictionary<string, string?> environment,
        IDictionary<string, string?>? lowerPrecedence = null)
    {
        var builder = new ConfigurationBuilder();

        if (lowerPrecedence is not null)
        {
            builder.AddInMemoryCollection(lowerPrecedence);
        }

        return builder
            .AddOmsLoanFlatEnvironmentSecrets(name => environment.TryGetValue(name, out var v) ? v : null)
            .Build();
    }

    /// <summary>
    /// The spellings themselves. OPEN_API_KEY is the one that looks like a typo and is not:
    /// it is what is set on the machines, so it is what the Worker reads.
    /// </summary>
    [Theory]
    [InlineData("Extraction:Claude:ApiKey", "CLAUDE_API_KEY")]
    [InlineData("Extraction:OpenAi:ApiKey", "OPEN_API_KEY")]
    [InlineData("Extraction:Groq:ApiKey", "GROQ_API_KEY")]
    [InlineData("Graph:TenantId", "GRAPH_TENANT_ID")]
    [InlineData("Graph:ClientId", "GRAPH_CLIENT_ID")]
    [InlineData("Graph:ClientSecret", "GRAPH_CLIENT_SECRET")]
    public void CanonicalSecretsKeepTheirAgreedVariableNames(string configurationKey, string variable)
    {
        var secret = Assert.Single(
            ConfigurationKeys.AllSecrets,
            candidate => candidate.ConfigurationKey == configurationKey);

        Assert.Equal(variable, secret.EnvironmentVariable);
    }

    [Fact]
    public void AllSecretsCoversProvidersAndGraphAndNothingElse()
    {
        Assert.Equal(6, ConfigurationKeys.AllSecrets.Count);
        Assert.Equal(3, ConfigurationKeys.ProviderApiKeys.Count);
        Assert.Equal(3, ConfigurationKeys.GraphSettings.Count);

        // Duplicate variable names would mean one secret silently overwriting another.
        Assert.Equal(
            ConfigurationKeys.AllSecrets.Count,
            ConfigurationKeys.AllSecrets.Select(s => s.EnvironmentVariable).Distinct().Count());
    }

    [Fact]
    public void FlatVariableSuppliesTheHierarchicalKey()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["CLAUDE_API_KEY"] = "from-flat",
            ["GRAPH_TENANT_ID"] = "tenant",
        });

        Assert.Equal("from-flat", configuration["Extraction:Claude:ApiKey"]);
        Assert.Equal("tenant", configuration["Graph:TenantId"]);
    }

    /// <summary>
    /// The precedence rule, stated as a test. The environment-variable provider turns
    /// <c>Extraction__Claude__ApiKey</c> into the same hierarchical key, so a machine
    /// carrying both spellings has to resolve to the flat one — otherwise a stale nested
    /// variable left over from an older deployment quietly wins.
    /// </summary>
    [Fact]
    public void FlatVariableBeatsTheNestedDoubleUnderscoreForm()
    {
        var configuration = Build(
            environment: new Dictionary<string, string?> { ["CLAUDE_API_KEY"] = "flat-wins" },
            lowerPrecedence: new Dictionary<string, string?> { ["Extraction:Claude:ApiKey"] = "nested-loses" });

        Assert.Equal("flat-wins", configuration["Extraction:Claude:ApiKey"]);
    }

    [Fact]
    public void FlatVariableBeatsAnAppSettingsValue()
    {
        var configuration = Build(
            environment: new Dictionary<string, string?> { ["GROQ_API_KEY"] = "flat-wins" },
            lowerPrecedence: new Dictionary<string, string?> { ["Extraction:Groq:ApiKey"] = "file-loses" });

        Assert.Equal("flat-wins", configuration["Extraction:Groq:ApiKey"]);
    }

    /// <summary>
    /// The committed placeholders are empty strings. If a blank variable were carried through
    /// it would outrank a real value from a lower-precedence source, and the banner would
    /// report a secret as present when nothing usable is there.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankVariableContributesNothingAndDoesNotShadow(string blank)
    {
        var configuration = Build(
            environment: new Dictionary<string, string?> { ["GROQ_API_KEY"] = blank },
            lowerPrecedence: new Dictionary<string, string?> { ["Extraction:Groq:ApiKey"] = "real-value" });

        Assert.Equal("real-value", configuration["Extraction:Groq:ApiKey"]);
    }

    [Fact]
    public void UnsetVariableLeavesTheKeyAbsent()
    {
        var configuration = Build(new Dictionary<string, string?>());

        foreach (var secret in ConfigurationKeys.AllSecrets)
        {
            Assert.Null(configuration[secret.ConfigurationKey]);
        }
    }

    /// <summary>
    /// The connection string deliberately did not move to a flat name — it keeps the .NET
    /// convention because the Api reads the same variable. Guards against someone "tidying"
    /// it into the flat set later.
    /// </summary>
    [Fact]
    public void ConnectionStringKeepsTheDoubleUnderscoreConvention()
    {
        Assert.Equal("ConnectionStrings:OmsLoan", ConfigurationKeys.ConnectionStringKey);
        Assert.Equal(
            "ConnectionStrings__OmsLoan",
            ConfigurationKeys.ToEnvironmentVariable(ConfigurationKeys.ConnectionStringKey));
        Assert.DoesNotContain(
            ConfigurationKeys.AllSecrets,
            secret => secret.ConfigurationKey == ConfigurationKeys.ConnectionStringKey);
    }
}
