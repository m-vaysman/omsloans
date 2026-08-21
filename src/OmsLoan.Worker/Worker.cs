namespace OmsLoan.Worker;

/// <summary>
/// Placeholder host for the ingestion loop. Folder polling, mailbox polling, and the
/// extraction calls are added by the ingestion and extractor issues.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OmsLoan ingestion worker started.");
        return Task.CompletedTask;
    }
}
