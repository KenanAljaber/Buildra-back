using Buildra.Domain.Agents;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Buildra.Application.Models;
namespace Buildra.Application.Execution;

public record ExecutionContext(ExecutionJob Job, Project Project, DevelopmentTask Task, AgentDefinition Developer, AgentDefinition Reviewer, Guid LeaseToken, string PreviousReview);
public record TaskExecutionDetails(DevelopmentTask Task, IReadOnlyList<Review> Reviews, IReadOnlyList<AgentRun> Runs, IReadOnlyList<ToolExecution> Tools, ExecutionJob? Job);
public interface IExecutionStore
{
    Task<Project?> GetProjectAsync(Guid org, Guid projectId, CancellationToken ct);
    Task SaveVerificationAsync(Guid org, Guid projectId, string repositoryUrl, string baseBranch, long repositoryId, CancellationToken ct);
    Task<bool> EnqueueAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct);
    Task<TaskExecutionDetails?> GetAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct);
    Task<ExecutionContext?> ClaimAsync(CancellationToken ct);
    Task<bool> RenewAsync(Guid jobId, Guid token, CancellationToken ct);
    Task<bool> OwnsAsync(ExecutionContext context, CancellationToken ct);
    Task<AgentRun> BeginRunAsync(ExecutionContext context, AgentDefinition agent, CancellationToken ct);
    Task RecordToolAsync(ExecutionContext context, Guid runId, string tool, string summary, bool succeeded, CancellationToken ct);
    Task RecordModelAsync(ExecutionContext context, Guid runId, ModelResponse response, CancellationToken ct);
    Task ProgressAsync(ExecutionContext context, AgentRun run, string activity, int step, bool recovery, CancellationToken ct);
    Task FinishRunAsync(ExecutionContext context, AgentRun run, string result, CancellationToken ct);
    Task SubmitImplementationAsync(ExecutionContext context, string branch, string commit, CancellationToken ct);
    Task SubmitReviewAsync(ExecutionContext context, bool approved, string summary, CancellationToken ct);
    Task CompleteAsync(ExecutionContext context, string pullRequestUrl, CancellationToken ct);
    Task FailAsync(ExecutionContext context, string error, CancellationToken ct);
}

public interface IWorkspaceTools
{
    IReadOnlyList<string> ListFiles(string root);
    string ReadFile(string root, string path);
    void WriteFile(string root, string path, string content);
    void DeleteFile(string root, string path);
    string SearchFiles(string root, string query);
    Task<TestResult> RunTestsAsync(string root, string image, string command, CancellationToken ct);
}
public record TestResult(bool Passed, string Output);
public sealed class ExecutionException(string safeMessage) : Exception(safeMessage);
