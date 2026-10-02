using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
namespace Buildra.Application.SourceControl;

public record RepositoryMetadata(long Id, string FullName, string DefaultBranch);
public record TaskWorkspace(string Root, string SourceDirectory, string Branch, string BaseCommit, IDisposable Lock);
public interface ISourceControlProvider
{
    Task<RepositoryMetadata> VerifyAsync(Project project, CancellationToken ct);
    Task<TaskWorkspace> PrepareAsync(Project project, DevelopmentTask task, CancellationToken ct);
    Task<string> DiffAsync(TaskWorkspace workspace, CancellationToken ct);
    Task<string> CommitAsync(TaskWorkspace workspace, string title, CancellationToken ct);
    Task<string> PublishAsync(Project project, DevelopmentTask task, TaskWorkspace workspace, string summary, CancellationToken ct);
}
public sealed class SourceControlException(string safeMessage) : Exception(safeMessage);
