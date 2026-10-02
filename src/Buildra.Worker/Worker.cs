namespace Buildra.Worker;

// Foundation host only. Durable job claiming and agent execution belong to milestone 4.
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Buildra worker foundation ready. Agent execution is not configured.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
