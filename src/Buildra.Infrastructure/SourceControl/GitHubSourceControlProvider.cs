using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buildra.Application.SourceControl;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Infrastructure.Execution;
namespace Buildra.Infrastructure.SourceControl;

public sealed class GitHubSourceControlProvider(HttpClient http, IGitHubCredentialSource credentials, GitWorkspace git) : ISourceControlProvider
{
    private static string Repository(Project project)
    {
        Buildra.Application.Projects.ProjectUseCases.Validate(new(project.Name, project.Description, project.RepositoryUrl, project.DefaultBranch, project.Instructions, project.TestImage, project.TestCommand));
        var parts = new Uri(project.RepositoryUrl).AbsolutePath.Trim('/');
        return parts.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[..^4] : parts;
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        var token = await credentials.GetAsync(ct);
        using var request = new HttpRequestMessage(method, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("Buildra/0.2"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new SourceControlException($"GitHub returned HTTP {(int)response.StatusCode}. Check repository access, the configured base branch, and pull-request permission.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }
    public async Task<RepositoryMetadata> VerifyAsync(Project project, CancellationToken ct)
    {
        var repository = Repository(project);
        using var response = await SendAsync(HttpMethod.Get, "repos/" + repository, null, ct); var root = response.RootElement;
        if (root.GetProperty("archived").GetBoolean() || !root.TryGetProperty("permissions", out var permissions) || !permissions.GetProperty("push").GetBoolean())
            throw new SourceControlException("This GitHub account needs write access to a non-archived repository.");
        using var branches = await SendAsync(HttpMethod.Get, "repos/" + repository + "/branches?per_page=1", null, ct);
        if (branches.RootElement.GetArrayLength() == 0)
            await git.InitializeEmptyAsync(project.RepositoryUrl, project.DefaultBranch, ct);
        using var branch = await SendAsync(HttpMethod.Get, "repos/" + repository + "/branches/" + Uri.EscapeDataString(project.DefaultBranch), null, ct);
        return new(root.GetProperty("id").GetInt64(), root.GetProperty("full_name").GetString()!, root.GetProperty("default_branch").GetString()!);
    }
    public async Task<TaskWorkspace> PrepareAsync(Project project, DevelopmentTask task, CancellationToken ct)
    {
        var metadata = await VerifyAsync(project, ct);
        if (metadata.Id != project.GitHubRepositoryId) throw new SourceControlException("Repository identity changed. Verify the repository again before running tasks.");
        return await git.PrepareAsync(project.RepositoryUrl, project.DefaultBranch, task.Id, ct);
    }
    public Task<string> DiffAsync(TaskWorkspace workspace, CancellationToken ct) => git.DiffAsync(workspace, ct);
    public Task<string> CommitAsync(TaskWorkspace workspace, string title, CancellationToken ct) => git.CommitAsync(workspace, title, ct);
    public async Task<string> PublishAsync(Project project, DevelopmentTask task, TaskWorkspace workspace, string summary, CancellationToken ct)
    {
        var repository = Repository(project);
        if (!string.IsNullOrWhiteSpace(await git.GitAsync(workspace.SourceDirectory, ct, "status", "--porcelain")) ||
            (await git.GitAsync(workspace.SourceDirectory, ct, "rev-parse", "HEAD")).Trim() != task.Commit)
            throw new SourceControlException("The workspace changed after review. Inspect and retry before publishing.");
        await git.GitAsync(workspace.SourceDirectory, ct, "push", "origin", "HEAD:refs/heads/" + workspace.Branch);
        var owner = repository.Split('/')[0];
        using var existing = await SendAsync(HttpMethod.Get, "repos/" + repository + "/pulls?state=open&head=" + Uri.EscapeDataString(owner + ":" + workspace.Branch) + "&base=" + Uri.EscapeDataString(project.DefaultBranch), null, ct);
        if (existing.RootElement.GetArrayLength() > 0) return existing.RootElement[0].GetProperty("html_url").GetString()!;
        using var created = await SendAsync(HttpMethod.Post, "repos/" + repository + "/pulls", new {
            title = task.Title, head = workspace.Branch, @base = project.DefaultBranch,
            body = $"{task.Description}\n\nAcceptance criteria:\n{task.AcceptanceCriteria}\n\nBuildra review:\n{summary}\n\nBranch: {workspace.Branch}\nCommit: {task.Commit}\n\nFinal merge requires the repository owner's review."
        }, ct);
        return created.RootElement.GetProperty("html_url").GetString()!;
    }
}
