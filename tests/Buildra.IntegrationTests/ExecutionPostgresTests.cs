using System.Text.Json;
using Buildra.Application.Execution;
using Buildra.Application.Models;
using Buildra.Application.Planning;
using Buildra.Application.SourceControl;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Buildra.Infrastructure.Execution;
using Buildra.Infrastructure.Planning;
using Buildra.Infrastructure.SourceControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Status = Buildra.Domain.Tasks.TaskStatus;
namespace Buildra.IntegrationTests;

public sealed class DockerPostgreSqlFactAttribute : FactAttribute
{
    public DockerPostgreSqlFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILDRA_TEST_POSTGRES")) || Environment.GetEnvironmentVariable("BUILDRA_TEST_DOCKER") != "1")
            Skip = "Set BUILDRA_TEST_POSTGRES and BUILDRA_TEST_DOCKER=1 to run real Git/Docker workflow tests.";
    }
}
public sealed partial class PlanningPostgresTests
{
    private async Task<DevelopmentTask> ReadyTask(Project project)
    {
        await using var db = Context(); var stored = (await db.Projects.FindAsync(project.Id))!;
        stored.RepositoryVerifiedAt = DateTimeOffset.UtcNow; stored.GitHubRepositoryId = 1;
        var task = new DevelopmentTask { ProjectId = project.Id, Title = "Add a tested sum function", Description = "Implement a sum module", AcceptanceCriteria = "sum(2,3) equals 5; built-in tests pass", CreatedBy = user };
        task.TransitionTo(Status.Ready); db.Tasks.Add(task); await db.SaveChangesAsync(); return task;
    }
    private sealed class ScriptProvider(IEnumerable<AgentAction> script) : IModelProvider
    {
        private readonly Queue<AgentAction> actions = new(script);
        private readonly List<string> written = [];
        private AgentAction? previous;
        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct)
        {
            using var context = JsonDocument.Parse(request.Context);
            var files = context.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetString()).ToArray();
            Assert.All(written, file => Assert.Contains(file, files));
            Assert.True(context.RootElement.GetProperty("remainingActions").GetInt32() > 0);
            if (previous?.Action is "writeFile" or "editFile")
            {
                var last = context.RootElement.GetProperty("recentToolResults").EnumerateArray().Last();
                Assert.Equal(previous.Content, last.GetProperty("arguments").GetProperty("content").GetString());
                Assert.Equal(previous.Query, last.GetProperty("arguments").GetProperty("query").GetString());
            }
            var action = actions.Count > 0 ? actions.Dequeue() : new AgentAction("complete", "", "", "", "Finished");
            if (action.Action == "writeFile") written.Add(action.Path);
            previous = action;
            return Task.FromResult(new ModelResponse(JsonSerializer.Serialize(action, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "test-model", 10, 5));
        }
    }
    private sealed class LocalSource(GitWorkspace git, string remote) : ISourceControlProvider
    {
        public int Published { get; private set; }
        public Task<RepositoryMetadata> VerifyAsync(Project p, CancellationToken ct) => Task.FromResult(new RepositoryMetadata(1, "test/repo", "main"));
        public Task<TaskWorkspace> PrepareAsync(Project p, DevelopmentTask t, CancellationToken ct) => git.PrepareAsync(remote, "main", t.Id, ct);
        public Task<string> DiffAsync(TaskWorkspace w, CancellationToken ct) => git.DiffAsync(w, ct);
        public Task<string> CommitAsync(TaskWorkspace w, string title, CancellationToken ct) => git.CommitAsync(w, title, ct);
        public async Task<string> PublishAsync(Project p, DevelopmentTask t, TaskWorkspace w, string summary, CancellationToken ct)
        { Published++; await git.GitAsync(w.SourceDirectory, ct, "push", "origin", "HEAD:refs/heads/" + w.Branch); return "https://github.com/test/repo/pull/1"; }
    }
    private sealed class LocalRepository : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "buildra-git-tests-" + Guid.NewGuid().ToString("N"));
        public string Seed => Path.Combine(Root, "seed"); public string Remote => Path.Combine(Root, "remote.git");
        public GitWorkspace Git { get; }
        public LocalRepository() { Directory.CreateDirectory(Root); Git = new(new BoundedProcess(), Options.Create(new GitHubOptions { WorkspaceRoot = Path.Combine(Root, "tasks") })); }
        public async Task InitializeAsync()
        {
            await Git.GitAsync(Root, default, "init", "--bare", "--initial-branch=main", Remote);
            await Git.GitAsync(Root, default, "init", "--initial-branch=main", Seed);
            await File.WriteAllTextAsync(Path.Combine(Seed, "README.md"), "Test repository");
            await Git.GitAsync(Seed, default, "add", "README.md");
            await Git.GitAsync(Seed, default, "-c", "user.name=Test", "-c", "user.email=test@localhost", "commit", "-m", "Initial commit");
            await Git.GitAsync(Seed, default, "remote", "add", "origin", Remote); await Git.GitAsync(Seed, default, "push", "origin", "main");
        }
        public ValueTask DisposeAsync()
        {
            var expected = Path.GetFullPath(Path.GetTempPath()); var resolved = Path.GetFullPath(Root);
            if (!resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("buildra-git-tests-")) throw new InvalidOperationException("Unexpected test cleanup path");
            if (Directory.Exists(resolved))
            {
                foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(resolved, true);
            }
            return ValueTask.CompletedTask;
        }
    }
    private static AgentAction Action(string name, string path = "", string content = "", string summary = "") => new(name, path, content, "", summary);

    [DockerPostgreSqlFact]
    public async Task DockerTransfersWindowsWorkspaceAndDetectsPassingAndFailingNestedTests()
    {
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buildra", "workspaces");
        var root = Path.Combine(parent, "validation-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        try
        {
            var files = new WorkspaceFiles(); var runner = new DockerWorkspaceTools(files, new BoundedProcess());
            Assert.Contains("Created a CommonJS Node scaffold", await runner.PrepareAsync(source, "node:24-alpine", "node --test", default));
            Assert.Contains("commonjs", files.Read(source, "package.json"));
            Assert.DoesNotContain(files.List(source), path => path.EndsWith(".test.cjs"));
            files.Write(source, "test/nested/transfer.test.cjs", "const test=require('node:test');const assert=require('node:assert/strict');test('source arrived from Windows',()=>assert.equal(7,7));");
            var passing = await runner.RunTestsAsync(source, "node:24-alpine", "node --test", default);
            Assert.True(passing.Passed, passing.Output); Assert.Contains("source arrived from Windows", passing.Output);
            files.Edit(source, "test/nested/transfer.test.cjs", "assert.equal(7,7)", "assert.equal(7,8)");
            var failing = await runner.RunTestsAsync(source, "node:24-alpine", "node --test", default);
            Assert.False(failing.Passed); Assert.Contains("source arrived from Windows", failing.Output);
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("validation-")) throw new InvalidOperationException("Invalid test cleanup path");
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EmptyRepositoryGetsOneInitialCommitAndSupportsTaskCheckout()
    {
        await using var repo = new LocalRepository();
        await repo.Git.GitAsync(repo.Root, default, "init", "--bare", "--initial-branch=main", repo.Remote);
        var initializer = new GitWorkspace(new BoundedProcess(), Options.Create(new GitHubOptions { WorkspaceRoot = Path.Combine(repo.Root, new string('x', 40)) }));
        await initializer.InitializeEmptyAsync(repo.Remote, "main", default);
        var first = await repo.Git.GitAsync(repo.Remote, default, "rev-parse", "refs/heads/main");
        Assert.Contains("Initialized by Buildra", await repo.Git.GitAsync(repo.Remote, default, "show", "main:README.md"));
        await initializer.InitializeEmptyAsync(repo.Remote, "main", default);
        Assert.Equal(first, await repo.Git.GitAsync(repo.Remote, default, "rev-parse", "refs/heads/main"));
        var workspace = await repo.Git.PrepareAsync(repo.Remote, "main", Guid.NewGuid(), default);
        using (workspace.Lock) Assert.True(File.Exists(Path.Combine(workspace.SourceDirectory, "README.md")));
    }

    [Fact]
    public async Task MissingBaseInExistingRepositoryIsNotInitialized()
    {
        await using var repo = new LocalRepository(); await repo.InitializeAsync();
        var before = await repo.Git.GitAsync(repo.Root, default, "ls-remote", repo.Remote);
        await repo.Git.InitializeEmptyAsync(repo.Remote, "new-base", default);
        Assert.Equal(before, await repo.Git.GitAsync(repo.Root, default, "ls-remote", repo.Remote));
    }

    [Fact]
    public async Task RepositoryWithOnlyTagsIsNotInitialized()
    {
        await using var repo = new LocalRepository(); await repo.InitializeAsync();
        await repo.Git.GitAsync(repo.Remote, default, "tag", "v1", "main");
        await repo.Git.GitAsync(repo.Remote, default, "update-ref", "-d", "refs/heads/main");
        var before = await repo.Git.GitAsync(repo.Root, default, "ls-remote", repo.Remote);
        await repo.Git.InitializeEmptyAsync(repo.Remote, "main", default);
        Assert.Equal(before, await repo.Git.GitAsync(repo.Root, default, "ls-remote", repo.Remote));
    }

    private sealed class TestCredential : IGitHubCredentialSource
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("test-secret-token");
    }
    private sealed class GitHubHandler(bool existingPr = false) : HttpMessageHandler
    {
        public int Posts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-secret-token", request.Headers.Authorization?.Parameter);
            Assert.Contains("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version"));
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("main", body.RootElement.GetProperty("base").GetString());
                Assert.StartsWith("buildra/task-", body.RootElement.GetProperty("head").GetString());
                Assert.Contains("Acceptance criteria", body.RootElement.GetProperty("body").GetString());
                return new(System.Net.HttpStatusCode.Created) { Content = new StringContent("{\"html_url\":\"https://github.com/test/repo/pull/1\"}") };
            }
            var content = request.RequestUri.AbsolutePath.EndsWith("/pulls")
                ? existingPr ? "[{\"html_url\":\"https://github.com/test/repo/pull/1\"}]" : "[]"
                : request.RequestUri.AbsolutePath.EndsWith("/branches") ? "[{\"name\":\"main\"}]"
                : request.RequestUri.AbsolutePath.Contains("/branches/") ? "{}"
                : "{\"id\":1,\"full_name\":\"test/repo\",\"default_branch\":\"main\",\"archived\":false,\"permissions\":{\"push\":true}}";
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(content) };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GitHubPublishingCreatesOrReusesPrForReviewedCommit(bool existingPr)
    {
        await using var repo = new LocalRepository(); await repo.InitializeAsync();
        var task = new DevelopmentTask { Title = "Add sum", Description = "A sum module", AcceptanceCriteria = "Sum tests pass" };
        var project = new Project { Name = "Test", RepositoryUrl = "https://github.com/test/repo.git", DefaultBranch = "main" };
        using var handler = new GitHubHandler(existingPr); using var http = new HttpClient(handler);
        var source = new GitHubSourceControlProvider(http, new TestCredential(), repo.Git);
        Assert.Equal(1, (await source.VerifyAsync(project, default)).Id);
        var workspace = await repo.Git.PrepareAsync(repo.Remote, "main", task.Id, default);
        using (workspace.Lock)
        {
            await File.WriteAllTextAsync(Path.Combine(workspace.SourceDirectory, "sum.js"), "module.exports=(a,b)=>a+b;");
            task.Commit = await repo.Git.CommitAsync(workspace, task.Title, default); task.Branch = workspace.Branch;
            Assert.Equal("https://github.com/test/repo/pull/1", await source.PublishAsync(project, task, workspace, "Reviewed and tested", default));
            Assert.Equal(existingPr ? 0 : 1, handler.Posts);
            await File.WriteAllTextAsync(Path.Combine(workspace.SourceDirectory, "sum.js"), "changed after review");
            await Assert.ThrowsAsync<SourceControlException>(() => source.PublishAsync(project, task, workspace, "Reviewed", default));
        }
    }

    [DockerPostgreSqlFact]
    public async Task DeveloperReviewerRevisionCreatesCommitAndKeepsMainUntouched()
    {
        await using var repo = new LocalRepository(); await repo.InitializeAsync();
        var mainBefore = await repo.Git.GitAsync(repo.Remote, default, "rev-parse", "refs/heads/main");
        var project = await CreateProject(); var task = await ReadyTask(project);
        await using var db = Context(); var store = new EfExecutionStore(db); await store.EnqueueAsync(org, project.Id, task.Id, default);
        var context = (await store.ClaimAsync(default))!; var source = new LocalSource(repo.Git, repo.Remote);
        var script = new[] {
            Action("writeFile", "sum.js", "module.exports = (a,b) => a+b;"),
            Action("writeFile", "sum.test.js", "const test=require('node:test');const assert=require('node:assert/strict');const sum=require('./sum');test('adds numbers',()=>assert.equal(sum(2,3),5));"),
            Action("runTests"), Action("complete", summary: "Added sum and passing tests."),
            Action("readFile", "sum.js"), Action("runTests"), Action("requestChanges", summary: "Document the exported function."),
            new AgentAction("editFile", "sum.js", "/** Adds two numbers. */\nmodule.exports = (a,b) => a+b;", "module.exports = (a,b) => a+b;", "Document the function."),
            Action("runTests"), Action("complete", summary: "Documented the function; tests pass."),
            Action("readFile", "sum.js"), Action("runTests"), Action("approve", summary: "Acceptance criteria and independent tests pass.")
        };
        await new ExecuteCodeWorkflow(store, new ScriptProvider(script), source, new DockerWorkspaceTools(new WorkspaceFiles(), new BoundedProcess())).ExecuteAsync(context, default);
        var details = (await store.GetAsync(org, project.Id, task.Id, default))!;
        Assert.Equal(Status.Completed, details.Task.Status); Assert.Equal(1, source.Published);
        Assert.Equal(2, details.Reviews.Count); Assert.Equal(ReviewStatus.ChangesRequested, details.Reviews[0].Status); Assert.Equal(ReviewStatus.Approved, details.Reviews[1].Status);
        Assert.Equal(4, details.Runs.Count); Assert.All(details.Runs, run => Assert.Equal(Buildra.Domain.Agents.AgentRunStatus.Completed, run.Status));
        Assert.All(details.Runs, run => { Assert.True(run.Step > 0); Assert.NotNull(run.UpdatedAt); Assert.Equal("Finished", run.Activity); });
        Assert.Contains(details.Tools, tool => tool.Tool == "runTests" && tool.Succeeded);
        Assert.Contains(details.Tools, tool => tool.Tool == "editFile" && tool.Succeeded);
        Assert.Equal(mainBefore, await repo.Git.GitAsync(repo.Remote, default, "rev-parse", "refs/heads/main"));
        Assert.False(File.Exists(Path.Combine(repo.Seed, "sum.js")));
        Assert.NotEmpty(await repo.Git.GitAsync(repo.Remote, default, "rev-parse", "refs/heads/" + details.Task.Branch));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), project.Id, task.Id, default));
    }

    [DockerPostgreSqlFact]
    public async Task NoTestsCannotBeApprovedOrPublished()
    {
        await using var repo = new LocalRepository(); await repo.InitializeAsync(); var project = await CreateProject(); var task = await ReadyTask(project);
        await using var db = Context(); var store = new EfExecutionStore(db); await store.EnqueueAsync(org, project.Id, task.Id, default); var context = (await store.ClaimAsync(default))!;
        var source = new LocalSource(repo.Git, repo.Remote);
        await new ExecuteCodeWorkflow(store, new ScriptProvider(new[] { Action("writeFile", "sum.js", "module.exports=(a,b)=>a+b;"), Action("runTests"), Action("complete", summary: "Done") }),
            source, new DockerWorkspaceTools(new WorkspaceFiles(), new BoundedProcess())).ExecuteAsync(context, default);
        Assert.Equal(0, source.Published); var details = (await store.GetAsync(org, project.Id, task.Id, default))!;
        Assert.Equal(Status.Failed, details.Task.Status); Assert.Empty(details.Reviews);
        Assert.Contains(details.Tools, tool => tool.Tool == "runTests" && !tool.Succeeded);
    }

    [PostgreSqlFact]
    public async Task ExecutionQueueRejectsDuplicatesAndStaleWorkers()
    {
        var project = await CreateProject(); var task = await ReadyTask(project); await using var first = Context(); var store = new EfExecutionStore(first);
        Assert.False(await store.EnqueueAsync(Guid.NewGuid(), project.Id, task.Id, default)); Assert.True(await store.EnqueueAsync(org, project.Id, task.Id, default));
        Assert.True(await store.EnqueueAsync(org, project.Id, task.Id, default));
        Assert.Equal(1, await first.ExecutionJobs.CountAsync());
        var job = (await store.ClaimAsync(default))!;
        await using var second = Context(); await second.ExecutionJobs.Where(j => j.Id == job.Job.Id).ExecuteUpdateAsync(u => u.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var recovered = (await new EfExecutionStore(second).ClaimAsync(default))!;
        Assert.NotEqual(job.LeaseToken, recovered.LeaseToken); Assert.False(await store.RenewAsync(job.Job.Id, job.LeaseToken, default)); Assert.False(await store.OwnsAsync(job, default));
        await Assert.ThrowsAsync<ExecutionException>(() => store.SubmitImplementationAsync(job, "branch", "commit", default));
    }

    [PostgreSqlFact]
    public async Task QueuedExecutionBlocksProjectEditsAndStaleVerification()
    {
        var project = await CreateProject(); var task = await ReadyTask(project);
        await using var db = Context(); var execution = new EfExecutionStore(db);
        await Assert.ThrowsAsync<SourceControlException>(() => execution.SaveVerificationAsync(org, project.Id, "https://github.com/other/repository", "main", 99, default));
        await execution.EnqueueAsync(org, project.Id, task.Id, default);
        var projects = new Buildra.Infrastructure.Projects.EfProjectStore(db);
        var details = (await projects.GetAsync(org, project.Id, default))!;
        details.Project.TestCommand = "echo changed";
        await Assert.ThrowsAsync<ArgumentException>(() => projects.SaveAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(() => projects.DeleteAsync(details.Project, default));
        await using var check = Context();
        Assert.Equal("node --test", (await check.Projects.FindAsync(project.Id))!.TestCommand);
    }

    [PostgreSqlFact]
    public async Task AutomaticExecutionUsesCurrentVerifiedProjectSettings()
    {
        var project = await CreateProject();
        await using (var db = Context())
        {
            var stored = (await db.Projects.FindAsync(project.Id))!;
            stored.AutoStartTasks = true; stored.RepositoryVerifiedAt = DateTimeOffset.UtcNow; stored.GitHubRepositoryId = 1;
            await db.SaveChangesAsync();
            await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default);
        }
        await using (var db = Context())
        {
            var store = new EfPlanningStore(db); var job = (await store.ClaimAsync(default))!;
            await using (var edit = Context()) await edit.Projects.Where(p => p.Id == project.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.AutoStartTasks, false));
            await store.CompleteAsync(job, Buildra.Application.Planning.TaskPlan.Parse(Plan), new ModelResponse(Plan, "test", 10, 5), default);
            Assert.False(await db.ExecutionJobs.AnyAsync());
        }
        await using (var db = Context())
        {
            await db.Projects.Where(p => p.Id == project.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.AutoStartTasks, true));
            await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search again", default);
        }
        await using (var db = Context())
        {
            await new ExecutePlanningJob(new EfPlanningStore(db), new Provider(Plan)).ExecuteNextAsync(default);
            Assert.Equal(ExecutionJobStatus.Queued, (await db.ExecutionJobs.SingleAsync()).Status);
        }
    }
}
