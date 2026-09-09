using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting.WindowsServices;
using OmsLoan.Api;
using OmsLoan.Domain;

// The SCM starts a service with C:\Windows\System32 as its working directory. Left alone,
// that becomes the content root: appsettings.json is never found, and — the part unique to a
// web host — WebRootPath is computed from it too, so wwwroot resolves to a folder that does
// not exist and the React build silently 404s while sitting next to the executable.
//
// AddWindowsService() below cannot fix this. Its IServiceCollection form runs after the host
// environment has already been computed; only the IHostBuilder form sets the content root,
// and WebApplicationBuilder does not use one. So it is set here, explicitly, and only when
// the process really is running under the SCM — leaving dotnet run and F5 to keep resolving
// the content root from the project directory, which is what makes launchSettings work.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// Reports lifecycle to the SCM: without it the SCM never learns the process finished
// starting, and kills it as hung. A no-op when the process is not running as a service, so
// the same build is what runs under dotnet run.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceMetadata.ServiceName;
});

// A service with no console needs somewhere to say why it stopped, and the Event Log is
// readable without deploying anything. The source is registered by the install script:
// creating one needs administrator rights that the service account is deliberately not
// granted.
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddOmsLoanEventLog();
}

// The SCM kills a service that does not stop in time and logs it as a crash. Twenty seconds
// is well inside the SCM's own patience and is more than enough to drain review requests,
// which are short reads and single-row writes rather than long work.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<UploadOptions>(
    builder.Configuration.GetSection(UploadOptions.SectionName));

// Cap the request body just above the upload limit, with room for the multipart envelope and
// the optional form fields. Without this the server's own default decides, independently of
// the configured limit, and an operator raising Upload:MaxBytes would find uploads still
// refused by a number they cannot see. The controller still checks the file itself, so a
// request inside this cap but over the limit gets a readable 413 rather than a connection
// closed mid-upload.
var maxUploadBytes = builder.Configuration
    .GetSection(UploadOptions.SectionName)
    .Get<UploadOptions>()?.MaxBytes ?? new UploadOptions().MaxBytes;

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes + (1 * 1024 * 1024);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes + (1 * 1024 * 1024);
});

// Configuration sources come from WebApplication.CreateBuilder in this order, lowest
// precedence first: appsettings.json, appsettings.{Environment}.json, user-secrets
// (Development only), environment variables, command line. Nothing is added here — the
// startup banner reports which one actually supplied each setting.
//
// Registered only when a connection string is present, matching the Worker: a review API
// that comes up and says it has no database is more useful than one that throws during
// startup and is restarted three times by the SCM before anyone reads a log.
var connectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStringName);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddOmsLoanDbContext(connectionString);
}

var app = builder.Build();

StartupSummary.Log(
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OmsLoan.Api.Startup"),
    app.Environment,
    builder.Configuration);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only when there is somewhere to redirect to. On an http-only self-host — the default for
// an internal deployment terminating TLS at a reverse proxy or not at all — this middleware
// cannot determine a port, logs a warning on every single request, and then does nothing.
if (HostUrls.HasHttpsEndpoint(app.Configuration))
{
    app.UseHttpsRedirection();
}

// Before routing, so a request for a hashed asset is answered without touching the endpoint
// pipeline. Skipped entirely when no build is present, which is every developer checkout.
if (SpaHosting.IsPresent(app.Environment))
{
    app.UseOmsLoanSpa();
}

app.UseAuthorization();

app.MapControllers();

// Last, and as fallbacks, so every controller route and every Swagger path wins over the
// SPA catch-all.
if (SpaHosting.IsPresent(app.Environment))
{
    app.MapOmsLoanSpaFallback();
}

app.Run();
