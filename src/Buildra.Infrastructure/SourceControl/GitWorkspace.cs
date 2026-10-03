using Buildra.Application.Execution;
using Buildra.Application.SourceControl;
using Buildra.Infrastructure.Execution;
using Microsoft.Extensions.Options;
namespace Buildra.Infrastructure.SourceControl;

public sealed class GitHubOptions
{
    public string WorkspaceRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buildra", "workspaces");
}
public sealed class GitWorkspace(BoundedProcess process, IOptions<GitHubOptions> options)
{
    public async Task InitializeEmptyAsync(string repositoryUrl, string baseBranch, CancellationToken ct)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repositoryUrl))).ToLowerInvariant();
        var root = Path.Combine(Path.GetFullPath(options.Value.WorkspaceRoot), "initialization", key);
        Directory.CreateDirectory(root);
        FileStream repositoryLock;
        try { repositoryLock = new FileStream(Path.Combine(root, "initialization.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new SourceControlException("Repository initialization is already running. Retry verification shortly."); }
        using (repositoryLock)
        {
            // Include tags and every advertised ref: a missing base branch alone never authorizes initialization.
            if (!string.IsNullOrWhiteSpace(await GitAsync(root, ct, "ls-remote", "--", repositoryUrl))) return;
            var source = Path.Combine(root, Guid.NewGuid().ToString("N"));
            await GitAsync(root, ct, "init", "--initial-branch=" + baseBranch, source);
            await File.WriteAllTextAsync(Path.Combine(source, "README.md"), "# Project\n\nInitialized by Buildra. Implementation changes are delivered through reviewed pull requests.\n", ct);
            await GitAsync(source, ct, "add", "--", "README.md");
            await GitAsync(source, ct, "-c", "user.name=Buildra", "-c", "user.email=buildra@localhost", "commit", "--no-gpg-sign", "-m", "Initialize repository with Buildra");
            await GitAsync(source, ct, "remote", "add", "origin", repositoryUrl);
            // A normal push rejects a competing, unrelated initial commit; existing history is never replaced.
            await GitAsync(source, ct, "push", "origin", "HEAD:refs/heads/" + baseBranch);
        }
    }
    public async Task<TaskWorkspace> PrepareAsync(string repositoryUrl, string baseBranch, Guid taskId, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetFullPath(options.Value.WorkspaceRoot), taskId.ToString("N")); Directory.CreateDirectory(root);
        FileStream taskLock;
        try { taskLock = new FileStream(Path.Combine(root, "execution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new SourceControlException("This task workspace is already in use by another worker. Retry after it stops."); }
        try
        {
            var source = Path.Combine(root, "source"); var branch = "buildra/task-" + taskId.ToString("N");
            var baseFile = Path.Combine(root, "base.commit");
            if (!Directory.Exists(Path.Combine(root, "control.git")))
            {
                await GitAsync(root, ct, "clone", "--no-checkout", "--separate-git-dir", Path.Combine(root, "control.git"), "--", repositoryUrl, source);
                await GitAsync(source, ct, "switch", "-c", branch, "origin/" + baseBranch);
                await File.WriteAllTextAsync(baseFile, (await GitAsync(source, ct, "rev-parse", "HEAD")).Trim(), ct);
            }
            if (!File.Exists(baseFile)) throw new SourceControlException("Workspace preparation was interrupted. Create a new task to preserve the incomplete workspace for inspection.");
            var remote = (await GitAsync(source, ct, "remote", "get-url", "origin")).Trim();
            if (remote != repositoryUrl) throw new SourceControlException("This workspace belongs to a different repository. Create a new task for the updated repository.");
            var actual = (await GitAsync(source, ct, "branch", "--show-current")).Trim();
            if (actual != branch) throw new SourceControlException("The task branch changed outside Buildra. Inspect the workspace before retrying.");
            return new(root, source, branch, (await File.ReadAllTextAsync(baseFile, ct)).Trim(), taskLock);
        }
        catch { taskLock.Dispose(); throw; }
    }
    public async Task<string> GitAsync(string directory, CancellationToken ct, params string[] args)
    {
        var noHooks = Path.Combine(Path.GetFullPath(options.Value.WorkspaceRoot), "no-hooks"); Directory.CreateDirectory(noHooks);
        var result = await process.RunAsync("git", new[] { "-c", "core.longpaths=true", "-c", "core.hooksPath=" + noHooks, "-c", "core.autocrlf=false", "-c", "diff.external=", "-c", "core.fsmonitor=false" }.Concat(args), directory, ct);
        if (result.ExitCode != 0) throw new SourceControlException("Git operation failed. Check repository access, the default branch, and workspace status.");
        return result.Output;
    }
    public Task<string> DiffAsync(TaskWorkspace workspace, CancellationToken ct) => GitAsync(workspace.SourceDirectory, ct, "diff", "--no-ext-diff", "--no-textconv", workspace.BaseCommit, "--", ".");
    public async Task RequireCurrentBaseAsync(TaskWorkspace workspace, string baseBranch, CancellationToken ct)
    {
        await GitAsync(workspace.SourceDirectory, ct, "fetch", "--no-tags", "origin", "refs/heads/" + baseBranch);
        var latest = (await GitAsync(workspace.SourceDirectory, ct, "rev-parse", "FETCH_HEAD")).Trim();
        var common = (await GitAsync(workspace.SourceDirectory, ct, "merge-base", "HEAD", latest)).Trim();
        if (common != latest)
            throw new SourceControlException("The base branch advanced beyond this implementation. Integrate the latest base branch, rerun tests, and obtain a new review before publishing. The task workspace is preserved.");
    }
    public async Task<string> CommitAsync(TaskWorkspace workspace, string title, CancellationToken ct)
    {
        await GitAsync(workspace.SourceDirectory, ct, "add", "--", ".");
        var status = await GitAsync(workspace.SourceDirectory, ct, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status))
            await GitAsync(workspace.SourceDirectory, ct, "-c", "user.name=Buildra", "-c", "user.email=buildra@localhost", "commit", "-m", title, "--no-gpg-sign");
        var commit = (await GitAsync(workspace.SourceDirectory, ct, "rev-parse", "HEAD")).Trim();
        if (commit == workspace.BaseCommit) throw new ExecutionException("The Developer produced no code changes.");
        return commit;
    }
}
