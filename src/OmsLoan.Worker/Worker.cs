using Microsoft.Extensions.Options;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker;

/// <summary>
/// Polls the watched folder and the shared mailbox on their own intervals.
/// </summary>
/// <remarks>
/// Polling rather than FileSystemWatcher. The watcher misses events when its buffer overflows
/// on a bulk drop, does not fire reliably on network shares — where these folders usually
/// live — and cannot retry a file locked when the event arrived. A scan re-examines what is
/// still present, so a missed notice is self-correcting.
/// </remarks>
public class Worker : BackgroundService
{
    private readonly FolderIngestion _folderIngestion;
    private readonly EmailIngestion _emailIngestion;
    private readonly IngestionOptions _options;
    private readonly MailboxOptions _mailboxOptions;
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// Guards rather than a primary constructor. Both ingestion paths are required — null is a
    /// composition mistake. Fail at build time rather than as a NullReferenceException inside
    /// a caught-and-logged pass that looks like an ingestion fault.
    /// </summary>
    public Worker(
        FolderIngestion folderIngestion,
        EmailIngestion emailIngestion,
        IOptions<IngestionOptions> options,
        IOptions<MailboxOptions> mailboxOptions,
        ILogger<Worker> logger)
    {
        ArgumentNullException.ThrowIfNull(folderIngestion);
        ArgumentNullException.ThrowIfNull(emailIngestion);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mailboxOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _folderIngestion = folderIngestion;
        _emailIngestion = emailIngestion;
        _options = options.Value;
        _mailboxOptions = mailboxOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OmsLoan ingestion worker started. Watching {Folder} every {FolderInterval}. "
            + "Polling {Mailbox} every {MailboxInterval}.",
            _options.WatchedFolder,
            _options.PollInterval,
            _mailboxOptions.Mailbox,
            _mailboxOptions.PollInterval);

        // Two loops. Folder is local and cheap; mailbox is a network round trip that can hang
        // or throttle. One timer would let a stuck mailbox stall folder ingestion.
        var folder = PollAsync(
            _options.PollInterval,
            () => _folderIngestion.RunOnceAsync(stoppingToken),
            "folder",
            stoppingToken);

        var email = PollAsync(
            _mailboxOptions.PollInterval,
            () => _emailIngestion.RunOnceAsync(stoppingToken),
            "mailbox",
            stoppingToken);

        await Task.WhenAll(folder, email);
    }

    /// <summary>
    /// One ingestion pass immediately, then on an interval until shutdown.
    /// </summary>
    /// <remarks>
    /// Immediately first so a restart picks up a backlog rather than looking idle — which
    /// after a deploy is when somebody is watching.
    /// </remarks>
    private async Task PollAsync(
        TimeSpan interval,
        Func<Task> pass,
        string what,
        CancellationToken stoppingToken)
    {
        await RunSafelyAsync(pass, what, stoppingToken);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunSafelyAsync(pass, what, stoppingToken);
        }
    }

    /// <summary>
    /// A throwing pass must not end the service. Work stays in the folder/mailbox; the next
    /// tick retries. Stopping would turn a transient fault into an outage.
    /// </summary>
    private async Task RunSafelyAsync(Func<Task> pass, string what, CancellationToken stoppingToken)
    {
        try
        {
            await pass();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down — not a failure.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {What} ingestion pass failed. Retrying on the next poll.", what);
        }
    }
}
