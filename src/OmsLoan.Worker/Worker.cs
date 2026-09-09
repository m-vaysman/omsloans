using Microsoft.Extensions.Options;
using OmsLoan.Worker.Ingestion;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker;

/// <summary>
/// Polls the watched folder on an interval.
/// </summary>
/// <remarks>
/// Polling rather than FileSystemWatcher. The watcher misses events when its buffer
/// overflows during a bulk drop, does not fire reliably on network shares — which is where
/// these folders usually live — and gives no way to retry a file that was locked when the
/// event arrived. A scan re-examines everything still present, so a missed notice is
/// self-correcting: anything not yet recorded is still sitting there next time.
/// </remarks>
public class Worker : BackgroundService
{
    private readonly FolderIngestion _folderIngestion;
    private readonly EmailIngestion _emailIngestion;
    private readonly IngestionOptions _options;
    private readonly MailboxOptions _mailboxOptions;
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// Guards rather than a primary constructor. Both ingestion paths are required — the
    /// Worker has nothing to do without either — so a null here is a composition mistake, and
    /// it is worth failing at the point the service is built rather than on the first poll,
    /// where it would surface as a NullReferenceException inside a caught-and-logged pass and
    /// look like an ingestion fault.
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

        // Two loops rather than one. The folder is local and cheap to scan; the mailbox is a
        // network round trip that can hang or be throttled. Sharing a timer would let a slow
        // or unreachable mailbox stall folder ingestion, which has nothing to do with it.
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
    /// Runs one ingestion pass immediately, then on an interval until shutdown.
    /// </summary>
    /// <remarks>
    /// Immediately first, so a restart picks up a backlog rather than looking idle for an
    /// interval — which after a deployment is exactly when somebody is watching.
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
    /// A pass that throws must not end the service. Whatever went wrong, the files are still
    /// in the folder and the next tick will try again; stopping would turn a transient fault
    /// into an outage that needs somebody to notice.
    /// </summary>
    private async Task RunSafelyAsync(Func<Task> pass, string what, CancellationToken stoppingToken)
    {
        try
        {
            await pass();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {What} ingestion pass failed. Retrying on the next poll.", what);
        }
    }
}
