using Buildra.Domain.Agents;
using Buildra.Domain.Conversations;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Buildra.Application.Models;
namespace Buildra.Application.Planning;

public record PlanningWorkspace(IReadOnlyList<Message> Messages, IReadOnlyList<DevelopmentTask> Tasks, IReadOnlyList<AgentRun> Runs, IReadOnlyList<ExecutionJob>? ExecutionJobs = null);
public record PlanningJob(PlanningRequest Request, AgentDefinition Agent, Project Project, string UserRequest, IReadOnlyList<Message> History);
public interface IPlanningStore
{
    Task<PlanningWorkspace?> GetAsync(Guid organizationId, Guid projectId, CancellationToken ct);
    Task<AgentRun?> EnqueueAsync(Guid organizationId, Guid projectId, Guid userId, string content, CancellationToken ct);
    Task<bool> RetryAsync(Guid organizationId, Guid projectId, Guid runId, CancellationToken ct);
    Task<PlanningJob?> ClaimAsync(CancellationToken ct);
    Task CompleteAsync(PlanningJob job, TaskPlan plan, ModelResponse response, CancellationToken ct);
    Task FailAsync(PlanningJob job, string safeError, ModelResponse? response, CancellationToken ct);
}

public sealed class PlanningUseCases(IPlanningStore store)
{
    public Task<PlanningWorkspace?> GetAsync(Guid org, Guid projectId, CancellationToken ct) => store.GetAsync(org, projectId, ct);
    public Task<AgentRun?> EnqueueAsync(Guid org, Guid projectId, Guid userId, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 8000) throw new ArgumentException("Request must contain 1–8000 characters.");
        return store.EnqueueAsync(org, projectId, userId, content.Trim(), ct);
    }
    public Task<bool> RetryAsync(Guid org, Guid projectId, Guid runId, CancellationToken ct) => store.RetryAsync(org, projectId, runId, ct);
}
