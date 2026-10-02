using Buildra.Application.Planning;
namespace Buildra.Worker;

public sealed class Worker(IServiceScopeFactory scopes, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Buildra PM worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<ExecutePlanningJob>().ExecuteNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Worker persistence is unavailable. Retrying shortly."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
