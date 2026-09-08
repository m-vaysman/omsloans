namespace OmsLoan.Api;

/// <summary>
/// Configuration keys the Api expects to find, and the environment-variable spellings that
/// supply them in Production.
/// </summary>
/// <remarks>
/// .NET maps a colon-separated key onto a double-underscore environment variable, so
/// <c>ConnectionStrings:OmsLoan</c> is set as <c>ConnectionStrings__OmsLoan</c>. Naming both
/// forms here keeps the deployment runbook and the code reading from the same list.
///
/// <see cref="ConnectionStringName"/> is intentionally the same string the Worker uses. The
/// two processes are separate services over one database, so one variable name spelled the
/// same way in both means one thing to get right per host, and a connection string copied
/// between the two install scripts without editing. The constant is duplicated rather than
/// shared for the same reason <see cref="ServiceMetadata"/> is: the projects do not
/// reference each other, and pulling a configuration contract into OmsLoan.Domain would make
/// the domain model know about hosting.
///
/// Note the drift this does <em>not</em> resolve:
/// <c>OmsLoan.Domain.OmsLoanDbContextRegistration.ConnectionStringVariable</c> is
/// <c>OMSLOAN_CONNECTION</c>, a bare variable read only by the design-time factory that
/// backs <c>dotnet ef</c>. It never participates in a running host's configuration and is
/// left alone here.
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
