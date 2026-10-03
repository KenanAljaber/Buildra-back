using Buildra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Buildra.Worker;
public sealed class WorkerPulse(IServiceScopeFactory scopes, ILogger<WorkerPulse> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BuildraDbContext>().Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO \"WorkerHeartbeats\" (\"Id\", \"UpdatedAt\") VALUES ('agents', {DateTimeOffset.UtcNow}) ON CONFLICT (\"Id\") DO UPDATE SET \"UpdatedAt\" = EXCLUDED.\"UpdatedAt\"", ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Worker heartbeat could not be saved."); }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
