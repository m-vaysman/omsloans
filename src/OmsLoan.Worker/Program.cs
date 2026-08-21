using OmsLoan.Domain;
using OmsLoan.Worker;

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
// (Development only), environment variables, command line. Nothing is added here — the
// startup banner reports which one actually supplied each setting.
var connectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStringName);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddOmsLoanDbContext(connectionString);
}

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

StartupSummary.Log(
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OmsLoan.Worker.Startup"),
    builder.Environment,
    builder.Configuration);

host.Run();
