namespace OmsLoan.Worker;

/// <summary>
/// One secret the Worker needs: the key it is read from in code, and the environment
/// variable that supplies it on a machine.
/// </summary>
/// <param name="ConfigurationKey">
/// The hierarchical key the application binds against, e.g. <c>Extraction:Claude:ApiKey</c>.
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
/// Configuration keys the Worker expects to find, and the environment variables that supply
/// them.
/// </summary>
/// <remarks>
/// <para>
/// There are two spellings in play and it is worth being precise about why.
/// </para>
/// <para>
/// .NET's own convention maps a hierarchical key onto a double-underscore variable, so
/// <c>Extraction:Claude:ApiKey</c> would be set as <c>Extraction__Claude__ApiKey</c>. That
/// mapping is built into the environment-variable provider and still works. But the machines
/// this runs on already carry flat names — <c>CLAUDE_API_KEY</c>, <c>GRAPH_TENANT_ID</c> —
/// set for other tooling, and nothing auto-maps those onto the nested keys. A deployment
/// with the secrets already present would have looked, to the Worker, exactly like one with
/// no secrets at all.
/// </para>
/// <para>
/// So the flat names win. <see cref="FlatEnvironmentSecrets"/> projects them onto the
/// hierarchical keys below and is registered as the highest-precedence configuration source,
/// which keeps application code binding against a clean options shape while an operator only
/// ever has to think about the flat variable. Note the spelling of the OpenAI one:
/// <c>OPEN_API_KEY</c>, not <c>OPENAI_API_KEY</c>. It is what is set on the machines, so it
/// is what is read here.
/// </para>
/// <para>
/// The nested <c>Extraction__Claude__ApiKey</c> form is not removed and cannot be — it comes
/// free with the environment-variable provider. It simply loses to the flat name when both
/// are set. It is no longer documented or written by the install script.
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
        new("Extraction:Claude:ApiKey", "CLAUDE_API_KEY", "Claude"),
        new("Extraction:OpenAi:ApiKey", "OPEN_API_KEY", "OpenAI"),
        new("Extraction:Groq:ApiKey", "GROQ_API_KEY", "Groq"),
    ];

    /// <summary>
    /// Microsoft Graph application credentials, for shared-mailbox ingestion. Configuration
    /// and presence reporting only at this point — no Graph call is made yet.
    /// </summary>
    /// <remarks>
    /// These three are the credential. <c>GRAPH_USER</c> and <c>GRAPH_TEST_MAILBOX</c> also
    /// exist on the test machines and hold the same value — the mailbox address — but that
    /// is an address rather than a secret, and which mailbox to poll is an ingestion setting
    /// this issue does not cover. See docs/exchange-test-environment.md.
    /// </remarks>
    public static readonly IReadOnlyList<ConfiguredSetting> GraphSettings =
    [
        new("Graph:TenantId", "GRAPH_TENANT_ID", "Graph tenant"),
        new("Graph:ClientId", "GRAPH_CLIENT_ID", "Graph application"),
        new("Graph:ClientSecret", "GRAPH_CLIENT_SECRET", "Graph secret"),
    ];

    /// <summary>Every flat-named secret, in banner order.</summary>
    public static readonly IReadOnlyList<ConfiguredSetting> AllSecrets =
        [.. ProviderApiKeys, .. GraphSettings];

    /// <summary>
    /// The database, described the same way as a secret so it can sit in
    /// <see cref="RequiredSettings"/> alongside the Graph credentials.
    /// </summary>
    /// <remarks>
    /// Deliberately not in <see cref="AllSecrets"/>: it keeps the .NET double-underscore
    /// convention rather than a flat name, so the stock environment-variable provider
    /// already resolves it and <see cref="FlatEnvironmentSecrets"/> has no work to do.
    /// </remarks>
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

    public static readonly ConfiguredSetting ConnectionString =
        new(ConnectionStringKey, "ConnectionStrings__OmsLoan", "Database");

    /// <summary>
    /// Settings the Worker refuses to start without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The database and the Graph credential are the two things without which the Worker
    /// has no useful work to do: it cannot record a notice and it cannot collect one from
    /// the shared mailbox. Starting anyway means a service the SCM reports as Running that
    /// silently ingests nothing — the worst of the failure modes, because it looks healthy
    /// and the gap only surfaces when somebody asks why the review queue is empty.
    /// </para>
    /// <para>
    /// Graph is all-or-nothing. A tenant with no client secret is not a partially working
    /// credential, so a partial set fails exactly as a missing one does.
    /// </para>
    /// <para>
    /// The provider API keys in <see cref="ProviderApiKeys"/> are deliberately <em>not</em>
    /// here. They are individually optional — the point of putting three providers behind
    /// one interface is that any one of them will do — and a notice ingested but not yet
    /// extracted is a recoverable state that reprocessing fixes.
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
