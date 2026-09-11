using Microsoft.Extensions.Hosting.WindowsServices;
using OmsLoan.Domain;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;
using OmsLoan.Worker;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

var builder = Host.CreateApplicationBuilder(args);

// Sets the content root to the executable's folder and reports lifecycle to Windows Service
// Control Manager. Without it an installed Windows service inherits C:\Windows\System32 and
// silently finds no appsettings.json — the classic "runs under Visual Studio / `dotnet run`,
// dies as a service". A no-op when not running as a service, so Visual Studio still works.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceMetadata.ServiceName;
});

// An installed Windows service has no console; the Event Log is where it says why it stopped.
// The source is registered by the install script — creating one needs admin rights the service
// account is deliberately not granted.
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddOmsLoanEventLog();
}

// Windows Service Control Manager kills a service that does not stop in time and logs it as a
// crash. Ingestion is interruptible, so this only needs to finish the notice in hand.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

// Configuration sources come from Host.CreateApplicationBuilder. The startup banner reports
// which one supplied each setting.
//
// Added last, so it wins: flat secrets the machines already carry (CLAUDE_API_KEY,
// GRAPH_TENANT_ID, …) projected onto hierarchical keys. Without this, a host with every
// secret set looks identical to one with none — nothing maps a flat name onto
// Extraction:Claude:ApiKey. See FlatEnvironmentSecrets.cs.
builder.Configuration.AddOmsLoanFlatEnvironmentSecrets();

var connectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStringName);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddOmsLoanDbContext(connectionString);
}

// Extraction providers (#8/#9/#10, one implementation per #68). A provider with no key or
// model id is not registered, so nothing resolves an extractor certain to fail on first call,
// and an unconfigured Groq leaves the others working.
//
// Bound eagerly rather than through IOptions: registration must know which providers are
// configured while still building the container.
builder.Services.Configure<ExtractionOptions>(
    builder.Configuration.GetSection(ExtractionOptions.SectionName));

var extraction = new ExtractionOptions();
builder.Configuration.GetSection(ExtractionOptions.SectionName).Bind(extraction);

var extractionProviders = builder.Services.AddChatClientExtractors(extraction);

builder.Services.Configure<IngestionOptions>(
    builder.Configuration.GetSection(IngestionOptions.SectionName));

builder.Services.Configure<MailboxOptions>(
    builder.Configuration.GetSection(MailboxOptions.SectionName));

builder.Services.AddSingleton<INoticeStore, EfNoticeStore>();
builder.Services.AddSingleton<FolderIngestion>();
builder.Services.AddSingleton<IMailboxClient, GraphMailboxClient>();
builder.Services.AddSingleton<EmailIngestion>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var startupLogger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("OmsLoan.Worker.Startup");

StartupSummary.Log(startupLogger, builder.Environment, builder.Configuration);

// Named at startup rather than discovered on the first notice. A provider silently missing
// its key looks identical at runtime to one never asked for — the gap is extractions that
// never happened.
if (extractionProviders.Count > 0)
{
    startupLogger.LogInformation(
        "Extraction providers registered: {Providers}. Default: {Default}.",
        string.Join(", ", extractionProviders),
        extraction.DefaultProvider);

    // A default naming an unregistered provider is the quiet version of having none. The
    // banner would look healthy; the first notice would throw resolving a name nothing answers.
    if (!extractionProviders.Contains(extraction.DefaultProvider, StringComparer.OrdinalIgnoreCase))
    {
        startupLogger.LogWarning(
            "The default extraction provider is {Default}, which is not registered. Registered: "
            + "{Providers}. Any extraction that does not name a provider explicitly will fail. "
            + "Either fix Extraction:DefaultProvider or supply that provider's key and model id.",
            extraction.DefaultProvider,
            string.Join(", ", extractionProviders));
    }
}
else
{
    startupLogger.LogWarning(
        "No extraction provider is configured, so notices will be ingested and not read. "
        + "A provider needs both an API key and a model id. This is a warning and not a "
        + "refusal to start: ingestion is still worth doing without extraction.");
}

// After the banner, before Run(): a Worker with no database or Graph credential must never
// reach Windows Service Control Manager as Running. See StartupValidation for why these two
// are fatal where a missing provider key is only a warning — and why this returns rather than
// throws.
var missing = StartupValidation.MissingRequiredSettings(builder.Configuration);

if (missing.Count > 0)
{
    StartupValidation.LogRefusalToStart(startupLogger, missing);

    // Dispose flushes logging providers. The console provider batches writes; returning from
    // Main can otherwise discard the message that explains the refusal.
    host.Dispose();

    return StartupValidation.ExitCodeFor(WindowsServiceHelpers.IsWindowsService());
}

// Prove the watched folder: create if missing, and check read and write. A folder that
// exists but cannot be written is the common case — fail here, not on the first notice.
// See WatchedFolder.
var folderProblem = WatchedFolder.Prepare(
    builder.Configuration[ConfigurationKeys.WatchedFolder.ConfigurationKey],
    builder.Configuration[ConfigurationKeys.ArchiveFolderKey]);

if (folderProblem is not null)
{
    StartupValidation.LogRefusalToStart(startupLogger, folderProblem);
    host.Dispose();

    return StartupValidation.ExitCodeFor(WindowsServiceHelpers.IsWindowsService());
}

host.Run();

return 0;
