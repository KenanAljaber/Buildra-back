using System.Text.Json;
using Buildra.Application.Execution;
using Buildra.Application.Models;
using Buildra.Application.SourceControl;
using Buildra.Domain.Agents;
using Buildra.Domain.Conversations;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Buildra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Context = Buildra.Application.Execution.ExecutionContext;
using Status = Buildra.Domain.Tasks.TaskStatus;
namespace Buildra.Infrastructure.Execution;

public sealed class EfExecutionStore(BuildraDbContext db) : IExecutionStore
{
    public Task<Project?> GetProjectAsync(Guid org, Guid id, CancellationToken ct) => db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.OrganizationId == org, ct);
    public async Task SaveVerificationAsync(Guid org, Guid id, string repositoryUrl, string baseBranch, long repositoryId, CancellationToken ct)
    {
        var updated = await db.Projects.Where(p => p.Id == id && p.OrganizationId == org && p.RepositoryUrl == repositoryUrl && p.DefaultBranch == baseBranch)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.RepositoryVerifiedAt, DateTimeOffset.UtcNow).SetProperty(p => p.GitHubRepositoryId, repositoryId), ct);
        if (updated != 1) throw new SourceControlException("Repository settings changed during verification. Verify again.");
    }
    public async Task<bool> EnqueueAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {projectId} AND \"OrganizationId\" = {org} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (project is null) return false;
        var task = await db.Tasks.FromSqlInterpolated($"SELECT * FROM \"Tasks\" WHERE \"Id\" = {taskId} AND \"ProjectId\" = {projectId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (task is null) return false;
        if (project.RepositoryVerifiedAt is null) throw new ExecutionException("Verify repository access before starting implementation.");
        var roles = await (from a in db.Agents join pa in db.ProjectAgents on a.Id equals pa.AgentDefinitionId
            where pa.ProjectId == projectId && pa.Enabled && a.OrganizationId == org select a.Role).ToListAsync(ct);
        if (!roles.Contains(AgentRole.Developer) || !roles.Contains(AgentRole.Reviewer)) throw new ExecutionException("Assign an enabled Developer and Reviewer first.");
        var job = await db.ExecutionJobs.SingleOrDefaultAsync(j => j.TaskId == taskId, ct);
        if (job?.Status is ExecutionJobStatus.Queued or ExecutionJobStatus.Running) { await tx.CommitAsync(ct); return true; }
        if (job?.Status == ExecutionJobStatus.Completed) throw new ExecutionException("This task is already completed. Open its pull request or create a new task.");
        if (task.Status == Status.Failed) task.RetryExecution();
        if (task.Status is not (Status.Ready or Status.ChangesRequested)) throw new ExecutionException("Only ready tasks or failed implementations can be started.");
        if (job is null) db.ExecutionJobs.Add(new() { TaskId = taskId, ProjectId = projectId });
        else { job.Status = ExecutionJobStatus.Queued; job.Error = null; job.LeaseToken = null; job.LeaseExpiresAt = null; }
        Event(project, taskId, "ImplementationQueued"); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<TaskExecutionDetails?> GetAsync(Guid org, Guid projectId, Guid taskId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OrganizationId == org, ct)) return null;
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct); if (task is null) return null;
        var runs = await db.AgentRuns.AsNoTracking().Where(r => r.TaskId == taskId).OrderBy(r => r.StartedAt).ToListAsync(ct);
        var ids = runs.Select(r => r.Id).ToArray();
        return new(task, await db.Reviews.AsNoTracking().Where(r => r.TaskId == taskId).OrderBy(r => r.CreatedAt).ToListAsync(ct), runs,
            await db.ToolExecutions.AsNoTracking().Where(t => ids.Contains(t.AgentRunId)).OrderBy(t => t.OccurredAt).Take(150).ToListAsync(ct),
            await db.ExecutionJobs.AsNoTracking().SingleOrDefaultAsync(j => j.TaskId == taskId, ct));
    }
    public async Task<Context?> ClaimAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(782346192)", ct);
        var candidates = await db.ExecutionJobs.FromSqlRaw("""
            SELECT * FROM "ExecutionJobs" j WHERE (j."Status" = 0 OR (j."Status" = 1 AND j."LeaseExpiresAt" < now()))
            AND NOT EXISTS (SELECT 1 FROM "ExecutionJobs" active WHERE active."ProjectId" = j."ProjectId" AND active."Id" <> j."Id"
                AND active."Status" = 1 AND active."LeaseExpiresAt" > now())
            ORDER BY j."CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        var job = candidates.SingleOrDefault(); if (job is null) { await tx.CommitAsync(ct); return null; }
        job.Status = ExecutionJobStatus.Running; job.LeaseToken = Guid.NewGuid(); job.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3);
        var project = await db.Projects.SingleAsync(p => p.Id == job.ProjectId, ct); var task = await db.Tasks.SingleAsync(t => t.Id == job.TaskId, ct);
        var agents = await (from a in db.Agents join pa in db.ProjectAgents on a.Id equals pa.AgentDefinitionId
            where pa.ProjectId == project.Id && pa.Enabled && a.OrganizationId == project.OrganizationId select a).ToListAsync(ct);
        var developer = agents.SingleOrDefault(a => a.Role == AgentRole.Developer); var reviewer = agents.SingleOrDefault(a => a.Role == AgentRole.Reviewer);
        if (developer is null || reviewer is null)
        {
            job.Status = ExecutionJobStatus.Failed; job.Error = "An enabled Developer and Reviewer are required.";
            task.TransitionTo(Status.Failed); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return null;
        }
        foreach (var run in await db.AgentRuns.Where(r => r.TaskId == task.Id && r.Status == AgentRunStatus.Running).ToListAsync(ct))
        { run.Status = AgentRunStatus.Failed; run.CompletedAt = DateTimeOffset.UtcNow; run.Error = "Worker interrupted; execution recovered."; }
        var previousReview = await db.Reviews.Where(r => r.TaskId == task.Id).OrderByDescending(r => r.CreatedAt).Select(r => r.Summary).FirstOrDefaultAsync(ct) ?? "";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(job, project, task, developer, reviewer, job.LeaseToken.Value, previousReview);
    }
    public async Task<bool> RenewAsync(Guid id, Guid token, CancellationToken ct) => await db.ExecutionJobs
        .Where(j => j.Id == id && j.LeaseToken == token && j.Status == ExecutionJobStatus.Running && j.LeaseExpiresAt > DateTimeOffset.UtcNow)
        .ExecuteUpdateAsync(u => u.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(3)), ct) == 1;
    public Task<bool> OwnsAsync(Context c, CancellationToken ct) => db.ExecutionJobs.AsNoTracking().AnyAsync(j => j.Id == c.Job.Id && j.LeaseToken == c.LeaseToken && j.Status == ExecutionJobStatus.Running && j.LeaseExpiresAt > DateTimeOffset.UtcNow, ct);
    private async Task LockedAsync(Context c, Action action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.ExecutionJobs.FromSqlInterpolated($"SELECT * FROM \"ExecutionJobs\" WHERE \"Id\" = {c.Job.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (job?.Status != ExecutionJobStatus.Running || job.LeaseToken != c.LeaseToken || job.LeaseExpiresAt < DateTimeOffset.UtcNow)
            throw new ExecutionException("The task lease was lost; the stale worker stopped.");
        action(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<AgentRun> BeginRunAsync(Context c, AgentDefinition agent, CancellationToken ct)
    {
        if (agent.Role == AgentRole.Developer)
        {
            if (!agent.Allows(AgentPermission.ReadRepository | AgentPermission.WriteRepository | AgentPermission.RunTests | AgentPermission.Commit | AgentPermission.Push))
                throw new ExecutionException("The Developer lacks required code, test, commit, or push permission.");
        }
        else if (agent.Role != AgentRole.Reviewer || !agent.Allows(AgentPermission.ReadRepository | AgentPermission.RunTests | AgentPermission.SubmitReview) || agent.Allows(AgentPermission.WriteRepository))
            throw new ExecutionException("Reviewer permissions must be read/test/review only.");
        var run = new AgentRun { ProjectId = c.Project.Id, TaskId = c.Task.Id, AgentDefinitionId = agent.Id, Status = AgentRunStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        await LockedAsync(c, () => {
            if (agent.Role == AgentRole.Developer)
            {
                if (c.Task.Status == Status.InReview) c.Task.TransitionTo(Status.ChangesRequested);
                if (c.Task.Status is Status.Ready or Status.ChangesRequested) c.Task.TransitionTo(Status.InDevelopment);
            }
            db.AgentRuns.Add(run); Event(c.Project, c.Task.Id, "AgentRunStarted");
        }, ct); return run;
    }
    public Task RecordToolAsync(Context c, Guid runId, string tool, string summary, bool succeeded, CancellationToken ct) => LockedAsync(c,
        () => db.ToolExecutions.Add(new() { AgentRunId = runId, Tool = tool, Summary = summary.Length > 500 ? summary[..500] : summary, Succeeded = succeeded }), ct);
    public async Task RecordModelAsync(Context c, Guid runId, ModelResponse response, CancellationToken ct)
    {
        var run = await db.AgentRuns.SingleAsync(r => r.Id == runId, ct);
        await LockedAsync(c, () => {
            run.Model = response.Model; run.InputTokens += response.InputTokens; run.OutputTokens += response.OutputTokens;
            run.EstimatedCost = response.EstimatedCost is null ? null : (run.EstimatedCost ?? 0) + response.EstimatedCost;
        }, ct);
    }
    public Task FinishRunAsync(Context c, AgentRun run, string result, CancellationToken ct) => LockedAsync(c, () => {
        run.Status = AgentRunStatus.Completed; run.Result = result; run.CompletedAt = DateTimeOffset.UtcNow; run.Activity = "Finished"; run.UpdatedAt = DateTimeOffset.UtcNow;
    }, ct);
    public Task ProgressAsync(Context c, AgentRun run, string activity, int step, bool recovery, CancellationToken ct) => LockedAsync(c, () => {
        run.Activity = activity; run.Step = step; run.UpdatedAt = DateTimeOffset.UtcNow; if (recovery) run.Recoveries++;
    }, ct);
    public Task SubmitImplementationAsync(Context c, string branch, string commit, CancellationToken ct) => LockedAsync(c, () => {
        c.Task.Branch = branch; c.Task.Commit = commit; c.Task.TransitionTo(Status.InReview); Event(c.Project, c.Task.Id, "ImplementationSubmitted");
    }, ct);
    public Task SubmitReviewAsync(Context c, bool approved, string summary, CancellationToken ct) => LockedAsync(c, () => {
        db.Reviews.Add(new() { TaskId = c.Task.Id, ReviewerAgentId = c.Reviewer.Id, Status = approved ? ReviewStatus.Approved : ReviewStatus.ChangesRequested,
            Summary = summary, Findings = approved ? "" : summary });
        c.Task.TransitionTo(approved ? Status.Approved : Status.ChangesRequested); Event(c.Project, c.Task.Id, approved ? "TaskApproved" : "ReviewChangesRequested");
    }, ct);
    public Task CompleteAsync(Context c, string url, CancellationToken ct) => LockedAsync(c, () => {
        c.Task.PullRequestUrl = url; c.Task.TransitionTo(Status.Completed); c.Job.Status = ExecutionJobStatus.Completed; c.Job.LeaseExpiresAt = null;
        Event(c.Project, c.Task.Id, "PullRequestCreated", JsonSerializer.Serialize(new { url }));
        AddMessage(c, $"Reviewed implementation ready: {url}");
    }, ct);
    public async Task FailAsync(Context c, string error, CancellationToken ct)
    {
        var runs = await db.AgentRuns.Where(r => r.TaskId == c.Task.Id && r.Status == AgentRunStatus.Running).ToListAsync(ct);
        await LockedAsync(c, () => {
            c.Job.Status = ExecutionJobStatus.Failed; c.Job.Error = error; c.Job.LeaseExpiresAt = null;
            if (c.Task.Status != Status.Completed) c.Task.TransitionTo(Status.Failed);
            foreach (var run in runs) { run.Status = AgentRunStatus.Failed; run.Error = error; run.CompletedAt = DateTimeOffset.UtcNow; }
            Event(c.Project, c.Task.Id, "AgentRunFailed"); AddMessage(c, error);
        }, ct);
    }
    private void Event(Project p, Guid taskId, string type, string payload = "{}") => db.WorkflowEvents.Add(new() { OrganizationId = p.OrganizationId, ProjectId = p.Id, TaskId = taskId, EventType = type, Payload = payload });
    private void AddMessage(Context c, string content)
    {
        var conversation = db.Conversations.Single(x => x.ProjectId == c.Project.Id && x.Type == ConversationType.Project);
        db.Messages.Add(new() { ConversationId = conversation.Id, SenderType = SenderType.System, Content = content, MessageType = c.Job.Status == ExecutionJobStatus.Failed ? "ActionRequired" : "Team" });
    }
}
