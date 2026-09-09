using Microsoft.Extensions.Options;
using OmsLoan.Worker.Ingestion;

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
    FolderIngestion ingestion,
    IOptions<IngestionOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollInterval;

        logger.LogInformation(
            "OmsLoan ingestion worker started. Watching {Folder} every {Interval}.",
            options.Value.WatchedFolder,
            interval);

        // Once before the first tick, so a restart picks up a backlog immediately rather than
        // after an interval of looking idle.
        await RunSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunSafelyAsync(stoppingToken);
        }
    }

    /// <summary>
    /// A pass that throws must not end the service. Whatever went wrong, the files are still
    /// in the folder and the next tick will try again; stopping would turn a transient fault
    /// into an outage that needs somebody to notice.
    /// </summary>
    private async Task RunSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ingestion.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion pass failed. Retrying on the next poll.");
        }
    }
}
