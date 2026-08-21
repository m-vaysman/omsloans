namespace OmsLoan.Worker;

/// <summary>
/// Configuration keys the Worker expects to find, and the environment-variable spellings
/// that supply them in Production.
/// </summary>
/// <remarks>
/// .NET maps a colon-separated key onto a double-underscore environment variable, so
/// <c>Extraction:Claude:ApiKey</c> is set as <c>Extraction__Claude__ApiKey</c>. Naming both
/// forms here keeps the deployment runbook and the code reading from the same list.
/// </remarks>
public static class ConfigurationKeys
{
    /// <summary>Named connection string the Worker resolves the database from.</summary>
    public const string ConnectionStringName = "OmsLoan";

    public const string ConnectionStringKey = "ConnectionStrings:" + ConnectionStringName;

    /// <summary>
    /// Provider API keys. Held here rather than in the extractor projects so the startup
    /// check can report a missing key before any notice is picked up, instead of surfacing
    /// it as a failed extraction hours later.
    /// </summary>
    public static readonly IReadOnlyList<string> ProviderApiKeys =
    [
        "Extraction:Claude:ApiKey",
        "Extraction:OpenAi:ApiKey",
        "Extraction:Groq:ApiKey",
    ];

    /// <summary>The environment-variable spelling of a configuration key.</summary>
    public static string ToEnvironmentVariable(string configurationKey) =>
        configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
