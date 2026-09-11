namespace OmsLoan.Worker;

/// <summary>
/// Reads flat machine-level secret variables and presents them under the hierarchical keys in
/// <see cref="ConfigurationKeys"/>.
/// </summary>
/// <remarks>
/// <para>
/// Machines already carry <c>CLAUDE_API_KEY</c>, <c>GRAPH_TENANT_ID</c>, and friends. .NET's
/// env-var provider only understands <c>Section__Key</c>, so those flat names reached the
/// Worker as nothing — a fully configured host looked bare.
/// </para>
/// <para>
/// Registered <em>last</em> so flat names win: outranks appsettings, user-secrets, and nested
/// forms. A stale nested variable cannot shadow the real one. Command-line values lose too —
/// right for a service never launched that way.
/// </para>
/// <para>
/// Unset or blank contributes nothing, so the banner can tell absent from empty.
/// </para>
/// </remarks>
internal static class FlatEnvironmentSecrets
{
    /// <summary>
    /// Adds the flat-variable source. <paramref name="lookup"/> exists so the mapping can be
    /// tested without writing to the real process environment; production passes nothing.
    /// </summary>
    public static IConfigurationBuilder AddOmsLoanFlatEnvironmentSecrets(
        this IConfigurationBuilder builder,
        Func<string, string?>? lookup = null)
    {
        builder.Add(new FlatEnvironmentSecretsSource(lookup ?? Environment.GetEnvironmentVariable));
        return builder;
    }
}

internal sealed class FlatEnvironmentSecretsSource(Func<string, string?> lookup) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new FlatEnvironmentSecretsProvider(lookup);
}

internal sealed class FlatEnvironmentSecretsProvider(Func<string, string?> lookup) : ConfigurationProvider
{
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var secret in ConfigurationKeys.AllSecrets)
        {
            var value = lookup(secret.EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[secret.ConfigurationKey] = value;
            }
        }

        Data = data;
    }

    /// <summary>
    /// Named for the startup banner, which prints the winning provider for each setting.
    /// "MemoryConfigurationProvider" would have been technically accurate and useless.
    /// </summary>
    public override string ToString() => "Flat environment secrets (CLAUDE_API_KEY, GRAPH_TENANT_ID, ...)";
}
