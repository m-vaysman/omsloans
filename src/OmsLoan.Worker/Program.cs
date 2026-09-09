using Microsoft.Extensions.Hosting.WindowsServices;
using OmsLoan.Domain;
using OmsLoan.Domain.Extractors;
using OmsLoan.Infrastructure.Extraction;
using OmsLoan.Worker;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

var builder = Host.CreateApplicationBuilder(args);

// Sets the content root to the executable's folder and reports lifecycle to the SCM. Without
// it a service started by the SCM inherits C:\Windows\System32 as its working directory and
// silently finds no appsettings.json — the classic "runs with dotnet run, dies as a service".
// It is a no-op when the process is not actually running as a service, so F5 still works.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceMetadata.ServiceName;
});

// The only sink that exists before the logging issue lands. A service with no console needs
// somewhere to say why it stopped, and the Event Log is readable without deploying anything.
// The source is registered by the install script: creating one needs administrator rights
// that the service account is deliberately not granted.
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddOmsLoanEventLog();
}

// The SCM kills a service that does not stop in time and logs it as a crash. Ingestion work
// is interruptible, so this only needs to cover finishing the notice in hand.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

// Configuration sources come from Host.CreateApplicationBuilder in this order, lowest
// precedence first: appsettings.json, appsettings.{Environment}.json, user-secrets
// (Development only), environment variables, command line. The startup banner reports which
// one actually supplied each setting.
//
// Added last, so it wins: the flat secret variables the machines already carry —
// CLAUDE_API_KEY, GRAPH_TENANT_ID and the rest — projected onto the hierarchical keys the
// application binds against. Without this, a host with every secret correctly set looks
// identical to one with none, because nothing maps a flat name onto Extraction:Claude:ApiKey.
// See FlatEnvironmentSecrets.cs.
builder.Configuration.AddOmsLoanFlatEnvironmentSecrets();

var connectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStringName);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddOmsLoanDbContext(connectionString);
}

// The extraction providers (#8, #9, #10, built as one implementation per #68). A provider
// with no key or no model id is not registered at all, so nothing can resolve an extractor
// that is certain to fail on its first call, and an unconfigured Groq leaves the others
// working rather than taking the pipeline down with it.
//
// Bound eagerly rather than through IOptions because registration has to know which providers
// are configured while it is still building the container.
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
// its key looks identical at runtime to one that is simply not being asked for, and the
// difference only surfaces as extractions that never happened.
if (extractionProviders.Count > 0)
{
    startupLogger.LogInformation(
        "Extraction providers registered: {Providers}. Default: {Default}.",
        string.Join(", ", extractionProviders),
        extraction.DefaultProvider);

    // A default naming a provider that did not register is the quiet version of having none.
    // The banner above would read as healthy, every provider listed would be real, and the
    // first notice of the day would throw resolving a name nothing answers to.
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

// After the banner, so the log shows what was resolved before it shows what was missing, and
// before Run(), so a Worker with no database or no Graph credential never reaches the SCM as
// Running. See StartupValidation for why these two are fatal where a missing provider API
// key is only a warning — and for why this reports and returns rather than throwing.
var missing = StartupValidation.MissingRequiredSettings(builder.Configuration);

if (missing.Count > 0)
{
    StartupValidation.LogRefusalToStart(startupLogger, missing);

    // Dispose flushes the logging providers. The console provider batches its writes, and
    // returning from Main would otherwise be quick enough to discard the message that
    // explains the whole thing.
    host.Dispose();

    return StartupValidation.ExitCodeFor(WindowsServiceHelpers.IsWindowsService());
}

// The watched folder, once we know a path was configured. Created if missing, and read and
// write are both proved — a folder that exists but cannot be written to is the common case,
// and it would otherwise fail on the first notice rather than here. See WatchedFolder.
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
