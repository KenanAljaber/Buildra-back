using Buildra.Application.Execution;
namespace Buildra.Worker;

public sealed class ExecutionWorker(IServiceScopeFactory scopes, ILogger<ExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Buildra code execution worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IExecutionStore>();
                var context = await store.ClaimAsync(stoppingToken);
                if (context is not null)
                {
                    using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); execution.CancelAfter(TimeSpan.FromMinutes(45));
                    var workflow = scope.ServiceProvider.GetRequiredService<ExecuteCodeWorkflow>().ExecuteAsync(context, execution.Token);
                    try
                    {
                        while (!workflow.IsCompleted)
                        {
                            if (await Task.WhenAny(workflow, Task.Delay(TimeSpan.FromSeconds(30), stoppingToken)) == workflow) break;
                            using var renewal = scopes.CreateScope();
                            if (!await renewal.ServiceProvider.GetRequiredService<IExecutionStore>().RenewAsync(context.Job.Id, context.LeaseToken, stoppingToken))
                            { execution.Cancel(); break; }
                        }
                        try { await workflow; }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                        { if (await store.OwnsAsync(context, stoppingToken)) await store.FailAsync(context, "Execution reached its time limit or lost its lease. Inspect the task and retry.", stoppingToken); }
                    }
                    finally { execution.Cancel(); try { await workflow; } catch (OperationCanceledException) { } }
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Code execution persistence is unavailable. Retrying shortly."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
