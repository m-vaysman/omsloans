namespace OmsLoan.Worker;

/// <summary>
/// One secret the Worker needs: the key it is read from in code, and the environment
/// variable that supplies it on a machine.
/// </summary>
/// <param name="ConfigurationKey">
/// The hierarchical key the application binds against, e.g.
/// <c>Extraction:Providers:Claude:ApiKey</c>.
/// </param>
/// <param name="EnvironmentVariable">
/// The variable an operator actually sets, e.g. <c>CLAUDE_API_KEY</c>. For the secrets these
/// spellings are fixed by what is already deployed on the machines rather than chosen here;
/// for settings introduced by this repository they follow .NET's own double-underscore
/// convention.
/// </param>
/// <param name="Purpose">Short label for the startup banner.</param>
public sealed record ConfiguredSetting(string ConfigurationKey, string EnvironmentVariable, string Purpose);

/// <summary>
/// Configuration keys the Worker expects, and the environment variables that supply them.
/// </summary>
/// <remarks>
/// <para>
/// Two spellings are in play.
/// </para>
/// <para>
/// .NET maps <c>Extraction:Providers:Claude:ApiKey</c> to
/// <c>Extraction__Providers__Claude__ApiKey</c>. That still works. But these machines already
/// carry flat names — <c>CLAUDE_API_KEY</c>, <c>GRAPH_TENANT_ID</c> — for other tooling, and
/// nothing auto-maps those onto nested keys. A host with every secret set looked identical to
/// one with none.
/// </para>
/// <para>
/// So flat names win. <see cref="FlatEnvironmentSecrets"/> projects them onto the hierarchical
/// keys below as the highest-precedence source. Note the OpenAI spelling: <c>OPEN_API_KEY</c>,
/// not <c>OPENAI_API_KEY</c> — it is what is on the machines.
/// </para>
/// <para>
/// The nested form is not removed — it comes free with the env-var provider — but loses to
/// the flat name when both are set. The install script no longer writes it.
/// </para>
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
    public static readonly IReadOnlyList<ConfiguredSetting> ProviderApiKeys =
    [
        new("Extraction:Providers:Claude:ApiKey", "CLAUDE_API_KEY", "Claude"),
        new("Extraction:Providers:OpenAi:ApiKey", "OPEN_API_KEY", "OpenAI"),
        new("Extraction:Providers:Groq:ApiKey", "GROQ_API_KEY", "Groq"),
    ];

    /// <summary>
    /// Microsoft Graph application credentials, for shared-mailbox ingestion. Configuration
    /// and presence reporting only at this point — no Graph call is made yet.
    /// </summary>
    /// <remarks>
    /// First three are the credential; fourth is the mailbox to poll — an address, not a
    /// secret, but required: mailbox ingestion has nowhere to look without it.
    /// <c>GRAPH_USER</c> is what the machines and <c>tools/GraphDaemonSmokeTest.linq</c> use;
    /// <c>GRAPH_TEST_MAILBOX</c> holds the same value on test machines and is not read.
    ///
    /// Watch scope on <c>GRAPH_USER</c>. Often set at <em>user</em> scope on a development
    /// machine, which an installed Windows service never sees — use <c>setx /M</c> on any host
    /// running the service. See docs/exchange-test-environment.md.
    /// </remarks>
    public static readonly IReadOnlyList<ConfiguredSetting> GraphSettings =
    [
        new("Graph:TenantId", "GRAPH_TENANT_ID", "Graph tenant"),
        new("Graph:ClientId", "GRAPH_CLIENT_ID", "Graph application"),
        new("Graph:ClientSecret", "GRAPH_CLIENT_SECRET", "Graph secret"),
        new("Graph:Mailbox", "GRAPH_USER", "Shared mailbox"),
    ];

    /// <summary>Every flat-named secret, in banner order.</summary>
    public static readonly IReadOnlyList<ConfiguredSetting> AllSecrets =
        [.. ProviderApiKeys, .. GraphSettings];

    /// <summary>
    /// Folder the Worker watches for notices, and creates on startup if it is missing.
    /// </summary>
    /// <remarks>
    /// Keeps .NET's double-underscore convention rather than a flat name, for the same reason
    /// the connection string does: the flat names exist only because those particular
    /// variables were already set on the machines for other tooling. Nothing was already
    /// called anything here, so there is no pre-existing spelling to honour.
    /// </remarks>
    public static readonly ConfiguredSetting WatchedFolder =
        new("Ingestion:WatchedFolder", "Ingestion__WatchedFolder", "Watched folder");

    /// <summary>
    /// Optional. Where <c>processed/</c> and <c>failed/</c> live; blank means under the
    /// watched folder. Not required, because the default is right nearly always.
    /// </summary>
    public const string ArchiveFolderKey = "Ingestion:ArchiveFolder";

    /// <summary>
    /// The database, described the same way as a secret so it can sit in
    /// <see cref="RequiredSettings"/> alongside the Graph credentials.
    /// </summary>
    /// <remarks>
    /// Deliberately not in <see cref="AllSecrets"/>: it keeps the .NET double-underscore
    /// convention rather than a flat name, so the stock environment-variable provider
    /// already resolves it and <see cref="FlatEnvironmentSecrets"/> has no work to do.
    /// </remarks>
    public static readonly ConfiguredSetting ConnectionString =
        new(ConnectionStringKey, "ConnectionStrings__OmsLoan", "Database");

    /// <summary>
    /// Settings the Worker refuses to start without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Database and Graph are required: without them the Worker cannot record or collect a
    /// notice. Starting anyway means Windows Service Control Manager reports Running while
    /// nothing is ingested — looks healthy until the review queue stays empty.
    /// </para>
    /// <para>
    /// Graph is all-or-nothing. A tenant with no client secret is not a partial credential.
    /// </para>
    /// <para>
    /// Provider API keys in <see cref="ProviderApiKeys"/> are deliberately <em>not</em> here.
    /// They are individually optional — any one will do — and ingested-but-not-extracted is
    /// recoverable by reprocessing.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<ConfiguredSetting> RequiredSettings =
        [ConnectionString, WatchedFolder, .. GraphSettings];

    /// <summary>
    /// The double-underscore environment-variable spelling of a hierarchical key.
    /// </summary>
    /// <remarks>
    /// Still used for the connection string, which keeps the .NET convention
    /// (<c>ConnectionStrings__OmsLoan</c>) because that is what is already deployed and the
    /// Api reads the same variable. Only the secrets in <see cref="AllSecrets"/> moved to
    /// flat names, and only because flat names already existed on the machines.
    /// </remarks>
    public static string ToEnvironmentVariable(string configurationKey) =>
        configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
