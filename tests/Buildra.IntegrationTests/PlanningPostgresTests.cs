using Buildra.Application.Models;
using Buildra.Application.Planning;
using Buildra.Domain.Agents;
using Buildra.Domain.Identity;
using Buildra.Domain.Projects;
using Buildra.Domain.Workflows;
using Buildra.Infrastructure.Persistence;
using Buildra.Infrastructure.Planning;
using Buildra.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Buildra.IntegrationTests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILDRA_TEST_POSTGRES"))) Skip = "Set BUILDRA_TEST_POSTGRES to run isolated PostgreSQL tests."; }
}

public sealed class PlanningPostgresTests : IAsyncLifetime
{
    private string? connectionString;
    private string? adminConnection;
    private readonly string databaseName = "buildra_test_" + Guid.NewGuid().ToString("N");
    private readonly Guid org = Guid.NewGuid();
    private readonly Guid user = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BUILDRA_TEST_POSTGRES");
        if (string.IsNullOrEmpty(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnection = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin); await create.ExecuteNonQueryAsync();
        builder.Database = databaseName; connectionString = builder.ConnectionString;
        await using var db = Context(); await db.Database.MigrateAsync();
        db.Organizations.Add(new Organization { Id = org, Name = "Test" });
        db.Users.Add(new User { Id = user, OrganizationId = org, Name = "Test owner", Email = "test@localhost" });
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        if (adminConnection is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }
    private BuildraDbContext Context() => new(new DbContextOptionsBuilder<BuildraDbContext>().UseNpgsql(connectionString).Options);
    private async Task<Project> CreateProject()
    {
        await using var db = Context();
        var project = new Project { OrganizationId = org, Name = "Test project", RepositoryUrl = "https://github.com/owner/repo" };
        await new EfProjectStore(db).AddAsync(project, default); return project;
    }
    private sealed class Provider(string result, bool fail = false) : IModelProvider
    {
        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct)
        {
            if (fail) throw new ModelProviderException("Test provider is unavailable.");
            return Task.FromResult(new ModelResponse(result, "test-model", 101, 42));
        }
    }
    private const string Plan = """{"title":"Add project search","description":"Filter projects by name","acceptanceCriteria":["Matching names are shown","Clearing the query restores all projects"],"summary":"I created a bounded search task.","needsClarification":false}""";

    [PostgreSqlFact]
    public async Task WorkerPersistsTaskConversationAndUsageThenProjectCanBeDeleted()
    {
        var project = await CreateProject();
        await using (var db = Context()) await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default);
        await using (var db = Context()) Assert.True(await new ExecutePlanningJob(new EfPlanningStore(db), new Provider(Plan)).ExecuteNextAsync(default));
        await using (var db = Context())
        {
            var workspace = await new EfPlanningStore(db).GetAsync(org, project.Id, default);
            var task = Assert.Single(workspace!.Tasks); Assert.Equal(Buildra.Domain.Tasks.TaskStatus.Ready, task.Status);
            var run = Assert.Single(workspace.Runs); Assert.Equal(AgentRunStatus.Completed, run.Status); Assert.Equal(task.Id, run.TaskId);
            Assert.Equal(101, run.InputTokens); Assert.Equal(42, run.OutputTokens);
            Assert.Equal(2, workspace.Messages.Count);
            Assert.Null(await new EfPlanningStore(db).GetAsync(Guid.NewGuid(), project.Id, default));
            await new EfProjectStore(db).DeleteAsync((await db.Projects.FindAsync(project.Id))!, default);
            Assert.False(await db.AgentRuns.AnyAsync(r => r.ProjectId == project.Id));
        }
    }

    [PostgreSqlFact]
    public async Task FailureCanBeRetriedWithoutDuplicatingUserMessage()
    {
        var project = await CreateProject(); Guid runId;
        await using (var db = Context()) runId = (await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default))!.Id;
        await using (var db = Context()) await new ExecutePlanningJob(new EfPlanningStore(db), new Provider("", true)).ExecuteNextAsync(default);
        await using (var db = Context())
        {
            Assert.False(await new EfPlanningStore(db).RetryAsync(Guid.NewGuid(), project.Id, runId, default));
            Assert.True(await new EfPlanningStore(db).RetryAsync(org, project.Id, runId, default));
        }
        await using (var db = Context()) await new ExecutePlanningJob(new EfPlanningStore(db), new Provider(Plan)).ExecuteNextAsync(default);
        await using (var db = Context())
        {
            var state = await new EfPlanningStore(db).GetAsync(org, project.Id, default);
            Assert.Single(state!.Tasks); Assert.Equal(AgentRunStatus.Completed, Assert.Single(state.Runs).Status);
            Assert.Single(state.Messages, m => m.SenderType == Buildra.Domain.Conversations.SenderType.User);
        }
    }

    [PostgreSqlFact]
    public async Task ExpiredLeaseIsRecoveredAndOldWorkerCannotCompleteIt()
    {
        var project = await CreateProject();
        await using (var db = Context()) await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default);
        await using var first = Context(); var job = (await new EfPlanningStore(first).ClaimAsync(default))!;
        await using (var db = Context())
            await db.PlanningRequests.Where(r => r.Id == job.Request.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await using var second = Context(); var recovered = (await new EfPlanningStore(second).ClaimAsync(default))!;
        Assert.NotEqual(job.Request.LeaseToken, recovered.Request.LeaseToken);
        await new EfPlanningStore(first).CompleteAsync(job, TaskPlan.Parse(Plan), new(Plan, "test", 1, 1), default);
        Assert.False(await second.Tasks.AnyAsync(t => t.ProjectId == project.Id));
        await new EfPlanningStore(second).CompleteAsync(recovered, TaskPlan.Parse(Plan), new(Plan, "test", 1, 1), default);
        Assert.Single(await second.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentWorkersClaimOnlyOneRequestForTheSameProject()
    {
        var project = await CreateProject();
        await using (var db = Context())
        {
            var store = new EfPlanningStore(db);
            await store.EnqueueAsync(org, project.Id, user, "First", default); await store.EnqueueAsync(org, project.Id, user, "Second", default);
        }
        await using var first = Context(); await using var second = Context();
        var jobs = await Task.WhenAll(new EfPlanningStore(first).ClaimAsync(default), new EfPlanningStore(second).ClaimAsync(default));
        Assert.Single(jobs, j => j is not null);
    }

    [PostgreSqlFact]
    public async Task ClarificationDoesNotCreateAnInventedTask()
    {
        var project = await CreateProject();
        await using (var db = Context()) await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Change everything", default);
        await using (var db = Context()) await new ExecutePlanningJob(new EfPlanningStore(db), new Provider("""{"title":"","description":"","acceptanceCriteria":[],"summary":"Which behavior should change first?","needsClarification":true}""")).ExecuteNextAsync(default);
        await using (var db = Context())
        {
            var workspace = (await new EfPlanningStore(db).GetAsync(org, project.Id, default))!;
            Assert.Empty(workspace.Tasks); Assert.Contains(workspace.Messages, m => m.MessageType == "ActionRequired");
        }
    }

    [PostgreSqlFact]
    public async Task InvalidPlanDoesNotCreateTaskAndRecordsUsageWithoutRawOutput()
    {
        var project = await CreateProject();
        await using (var db = Context()) await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default);
        await using (var db = Context()) await new ExecutePlanningJob(new EfPlanningStore(db), new Provider("sensitive invalid output")).ExecuteNextAsync(default);
        await using (var db = Context())
        {
            var state = (await new EfPlanningStore(db).GetAsync(org, project.Id, default))!;
            Assert.Empty(state.Tasks);
            var run = Assert.Single(state.Runs); Assert.Equal(AgentRunStatus.Failed, run.Status); Assert.Equal(101, run.InputTokens);
            Assert.Null(run.Result); Assert.DoesNotContain("sensitive", run.Error!);
        }
    }

    [PostgreSqlFact]
    public async Task PermissionRevokedAfterQueuePreventsModelCallAndTaskCreation()
    {
        var project = await CreateProject();
        await using (var db = Context())
        {
            var run = (await new EfPlanningStore(db).EnqueueAsync(org, project.Id, user, "Add search", default))!;
            var agent = (await db.Agents.FindAsync(run.AgentDefinitionId))!; agent.Permissions = AgentPermission.ReadRepository; await db.SaveChangesAsync();
        }
        await using (var db = Context()) await new ExecutePlanningJob(new EfPlanningStore(db), new Provider(Plan)).ExecuteNextAsync(default);
        await using (var db = Context())
        {
            var state = (await new EfPlanningStore(db).GetAsync(org, project.Id, default))!;
            Assert.Empty(state.Tasks); Assert.Equal(AgentRunStatus.Failed, Assert.Single(state.Runs).Status); Assert.Equal(0, state.Runs[0].InputTokens);
        }
    }
}
