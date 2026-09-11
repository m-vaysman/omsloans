using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting.WindowsServices;
using OmsLoan.Api;
using OmsLoan.Data.Postgres;
using OmsLoan.Domain;

// Windows Service Control Manager starts a service with C:\Windows\System32 as its working
// directory. Left alone, that becomes the content root: appsettings.json is never found, and
// WebRootPath is computed from it too — so wwwroot resolves to a missing folder and the React
// build silently 404s while sitting next to the executable.
//
// AddWindowsService() below cannot fix this. Its IServiceCollection form runs after the host
// environment is already computed; only the IHostBuilder form sets the content root, and
// WebApplicationBuilder does not use one. Set here only when running as an installed Windows
// service — leaving Visual Studio and `dotnet run` to keep resolving from the project directory.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// Reports lifecycle to Windows Service Control Manager: without it the manager never learns
// the process finished starting, and kills it as hung. A no-op under Visual Studio / `dotnet
// run`, so one build covers both starts.
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
// crash. Twenty seconds is inside its patience and enough to drain short review requests.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<UploadOptions>(
    builder.Configuration.GetSection(UploadOptions.SectionName));

// Cap the request body just above the upload limit, with room for the multipart envelope.
// Without this Kestrel's default decides independently of Upload:MaxBytes, and raising the
// limit still refuses uploads by a number nobody can see. The controller still checks the
// file, so an in-cap but over-limit request gets a readable 413 rather than a mid-upload cut.
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

// Configuration sources come from WebApplication.CreateBuilder (appsettings, environment,
// command line). The startup banner reports which one supplied each setting.
//
// DbContext registered only when a connection string is present, matching the Worker: a
// review API that starts and says it has no database beats one that throws and is restarted
// three times by Windows Service Control Manager before anyone reads a log.
var connectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStringName);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddOmsLoanDatabase(builder.Configuration, connectionString);
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

// Only when something is listening on https. On an http-only self-host this middleware cannot
// pick a port, logs a warning on every request, and then does nothing.
if (HostUrls.HasHttpsEndpoint(app.Configuration))
{
    app.UseHttpsRedirection();
}

// Before routing, so a hashed asset never touches the endpoint pipeline. Skipped when no SPA
// build is present — every Visual Studio / `dotnet run` checkout.
if (SpaHosting.IsPresent(app.Environment))
{
    app.UseOmsLoanSpa();
}

app.UseAuthorization();

app.MapControllers();

// Last, as fallbacks, so every controller and Swagger path wins over the SPA catch-all.
if (SpaHosting.IsPresent(app.Environment))
{
    app.MapOmsLoanSpaFallback();
}

app.Run();
