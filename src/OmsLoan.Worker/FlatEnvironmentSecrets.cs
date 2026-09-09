namespace OmsLoan.Worker;

/// <summary>
/// Reads the flat, machine-level secret variables and presents them to the rest of the
/// application under the hierarchical keys in <see cref="ConfigurationKeys"/>.
/// </summary>
/// <remarks>
/// <para>
/// The problem this solves: the machines already carry <c>CLAUDE_API_KEY</c>,
/// <c>GRAPH_TENANT_ID</c> and friends, set for other tooling. .NET's environment-variable
/// provider only understands its own <c>Section__Key</c> convention, so those flat names
/// reached the Worker as nothing at all — a host with every secret correctly configured was
/// indistinguishable from a bare one.
/// </para>
/// <para>
/// Registering this as the <em>last</em> configuration source is deliberate, and is what
/// "flat names win" means concretely: it outranks appsettings, user-secrets and the nested
/// <c>Extraction__Claude__ApiKey</c> form. A stale nested variable left on a machine cannot
/// shadow the real one. The trade is that a value passed on the command line loses too,
/// which is the right way round for a service that is never launched with one.
/// </para>
/// <para>
/// A variable that is unset or blank contributes nothing rather than an empty string, so
/// "absent" stays distinguishable from "present but empty" in the startup banner.
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
