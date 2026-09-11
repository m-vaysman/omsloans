namespace OmsLoan.Api;

/// <summary>
/// Configuration keys the Api expects, and the environment-variable spellings that supply
/// them in Production.
/// </summary>
/// <remarks>
/// .NET maps <c>ConnectionStrings:OmsLoan</c> to <c>ConnectionStrings__OmsLoan</c>. Naming
/// both forms here keeps the runbook and the code on the same list.
///
/// <see cref="ConnectionStringName"/> matches the Worker on purpose: two services, one
/// database, one variable spelling. Duplicated rather than shared for the same reason
/// <see cref="ServiceMetadata"/> is — the projects do not reference each other, and Domain
/// must not know about hosting.
///
/// Drift this does <em>not</em> resolve:
/// <c>OmsLoanDbContextRegistration.ConnectionStringVariable</c> (<c>OMSLOAN_CONNECTION</c>)
/// is design-time for <c>dotnet ef</c> only; it never participates in a running host.
/// </remarks>
public static class ConfigurationKeys
{
    /// <summary>Named connection string the Api resolves the database from.</summary>
    public const string ConnectionStringName = "OmsLoan";

    public const string ConnectionStringKey = "ConnectionStrings:" + ConnectionStringName;

    /// <summary>
    /// Flat list of listening addresses, semicolon-separated. Set on the service as
    /// <c>ASPNETCORE_URLS</c>; the host strips the <c>ASPNETCORE_</c> prefix, which is why
    /// the configuration key is the bare word.
    /// </summary>
    public const string UrlsKey = "Urls";

    /// <summary>
    /// The richer alternative to <see cref="UrlsKey"/>, used when an endpoint needs a
    /// certificate or a protocol override. Configured in appsettings rather than as a
    /// variable, and read here only so the startup banner can report it.
    /// </summary>
    public const string KestrelEndpointsSection = "Kestrel:Endpoints";

    /// <summary>The environment-variable spelling of a configuration key.</summary>
    public static string ToEnvironmentVariable(string configurationKey) =>
        configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
