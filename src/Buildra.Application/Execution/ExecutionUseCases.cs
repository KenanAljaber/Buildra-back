using Buildra.Application.SourceControl;
namespace Buildra.Application.Execution;

public sealed class ExecutionUseCases(IExecutionStore store, ISourceControlProvider source)
{
    public async Task<RepositoryMetadata?> VerifyAsync(Guid org, Guid projectId, CancellationToken ct)
    {
        var project = await store.GetProjectAsync(org, projectId, ct); if (project is null) return null;
        var metadata = await source.VerifyAsync(project, ct);
        await store.SaveVerificationAsync(org, projectId, project.RepositoryUrl, project.DefaultBranch, metadata.Id, ct); return metadata;
    }
    public Task<bool> StartAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct) => store.EnqueueAsync(org, projectId, taskId, ct);
    public Task<TaskExecutionDetails?> GetAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct) => store.GetAsync(org, projectId, taskId, ct);
}
