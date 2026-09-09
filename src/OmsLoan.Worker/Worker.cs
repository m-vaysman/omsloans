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
public class Worker(
    FolderIngestion folderIngestion,
    EmailIngestion emailIngestion,
    IOptions<IngestionOptions> options,
    IOptions<MailboxOptions> mailboxOptions,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mailbox = mailboxOptions.Value;

        logger.LogInformation(
            "OmsLoan ingestion worker started. Watching {Folder} every {Interval}. "
            + "Mailbox {Mailbox} {MailboxState}.",
            options.Value.WatchedFolder,
            options.Value.PollInterval,
            mailbox.Mailbox,
            mailbox.Enabled ? $"every {mailbox.PollInterval}" : "disabled");

        // Two loops rather than one. The folder is local and cheap to scan; the mailbox is a
        // network round trip that can hang or be throttled. Sharing a timer would let a slow
        // or unreachable mailbox stall folder ingestion, which has nothing to do with it.
        var folder = PollAsync(
            options.Value.PollInterval,
            () => folderIngestion.RunOnceAsync(stoppingToken),
            "folder",
            stoppingToken);

        var email = mailbox.Enabled
            ? PollAsync(
                mailbox.PollInterval,
                () => emailIngestion.RunOnceAsync(stoppingToken),
                "mailbox",
                stoppingToken)
            : Task.CompletedTask;

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
            logger.LogError(ex, "The {What} ingestion pass failed. Retrying on the next poll.", what);
        }
    }
}
