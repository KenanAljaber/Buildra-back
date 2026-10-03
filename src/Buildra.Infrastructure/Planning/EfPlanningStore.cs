using System.Text.Json;
using Buildra.Application.Models;
using Buildra.Application.Planning;
using Buildra.Domain.Agents;
using Buildra.Domain.Conversations;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Buildra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Buildra.Infrastructure.Planning;

public sealed class EfPlanningStore(BuildraDbContext db) : IPlanningStore
{
    public async Task<PlanningWorkspace?> GetAsync(Guid org, Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OrganizationId == org, ct)) return null;
        var conversations = db.Conversations.Where(c => c.ProjectId == projectId && c.Type == ConversationType.Project).Select(c => c.Id);
        var messages = await db.Messages.AsNoTracking().Where(m => conversations.Contains(m.ConversationId)).OrderByDescending(m => m.CreatedAt).Take(100).ToListAsync(ct);
        return new(messages.OrderBy(m => m.CreatedAt).ToList(),
            await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId).OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync(ct),
            await db.AgentRuns.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.StartedAt).Take(100).ToListAsync(ct),
            await db.ExecutionJobs.AsNoTracking().Where(j => j.ProjectId == projectId).OrderByDescending(j => j.CreatedAt).Take(100).ToListAsync(ct));
    }

    public async Task<AgentRun?> EnqueueAsync(Guid org, Guid projectId, Guid userId, string content, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OrganizationId == org, ct)) return null;
        var agent = await (from a in db.Agents join assignment in db.ProjectAgents on a.Id equals assignment.AgentDefinitionId
            where assignment.ProjectId == projectId && assignment.Enabled && a.Role == AgentRole.ProductManager && a.OrganizationId == org select a).SingleOrDefaultAsync(ct);
        if (agent is null || !agent.Allows(AgentPermission.CreateTask)) throw new InvalidOperationException("Assign an enabled Product Manager with task-creation permission first.");
        var conversation = await db.Conversations.SingleAsync(c => c.ProjectId == projectId && c.Type == ConversationType.Project && c.OrganizationId == org, ct);
        var message = new Message { ConversationId = conversation.Id, SenderType = SenderType.User, SenderId = userId, Content = content };
        var run = new AgentRun { AgentDefinitionId = agent.Id, ProjectId = projectId, Status = AgentRunStatus.Queued };
        db.Messages.Add(message); db.AgentRuns.Add(run);
        db.PlanningRequests.Add(new() { ProjectId = projectId, AgentRunId = run.Id, ConversationId = conversation.Id, MessageId = message.Id });
        db.WorkflowEvents.Add(new() { OrganizationId = org, ProjectId = projectId, EventType = "PlanningRequested", Payload = JsonSerializer.Serialize(new { runId = run.Id }) });
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<bool> RetryAsync(Guid org, Guid projectId, Guid runId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OrganizationId == org, ct)) return false;
        // Conditional update prevents two retries from racing with a worker claim.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.PlanningRequests.FromSqlInterpolated($"SELECT * FROM \"PlanningRequests\" WHERE \"ProjectId\" = {projectId} AND \"AgentRunId\" = {runId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (request is null || request.Status != PlanningRequestStatus.Failed) return false;
        var run = await db.AgentRuns.SingleAsync(r => r.Id == runId, ct);
        request.Status = PlanningRequestStatus.Queued; request.LeaseToken = null; request.LeaseExpiresAt = null;
        run.Status = AgentRunStatus.Queued; run.Error = null; run.CompletedAt = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<PlanningJob?> ClaimAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize the short claim transaction so a project cannot be claimed twice from concurrent snapshots.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(782346191)", ct);
        // PostgreSQL row locks make claiming durable and safe across worker processes.
        var candidates = await db.PlanningRequests.FromSqlRaw("""
            SELECT * FROM "PlanningRequests" r
            WHERE (r."Status" = 0 OR (r."Status" = 1 AND r."LeaseExpiresAt" < now()))
              AND NOT EXISTS (SELECT 1 FROM "PlanningRequests" active WHERE active."ProjectId" = r."ProjectId"
                AND active."Id" <> r."Id" AND active."Status" = 1 AND active."LeaseExpiresAt" > now())
            ORDER BY r."CreatedAt" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        var request = candidates.SingleOrDefault();
        if (request is null) { await transaction.CommitAsync(ct); return null; }
        request.Status = PlanningRequestStatus.Running; request.LeaseToken = Guid.NewGuid();
        request.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var run = await db.AgentRuns.SingleAsync(r => r.Id == request.AgentRunId, ct);
        run.Status = AgentRunStatus.Running; run.StartedAt = DateTimeOffset.UtcNow; run.Error = null; run.Activity = "Planning acceptance criteria"; run.UpdatedAt = DateTimeOffset.UtcNow;
        var project = await db.Projects.SingleAsync(p => p.Id == request.ProjectId, ct);
        var agent = await db.Agents.SingleAsync(a => a.Id == run.AgentDefinitionId, ct);
        var message = await db.Messages.SingleAsync(m => m.Id == request.MessageId, ct);
        var history = await db.Messages.AsNoTracking().Where(m => m.ConversationId == request.ConversationId && m.CreatedAt < message.CreatedAt)
            .OrderByDescending(m => m.CreatedAt).Take(12).ToListAsync(ct);
        db.WorkflowEvents.Add(new() { OrganizationId = project.OrganizationId, ProjectId = project.Id, EventType = "AgentRunStarted" });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(request, agent, project, message.Content, history.OrderBy(m => m.CreatedAt).ToList());
    }

    public async Task CompleteAsync(PlanningJob job, TaskPlan plan, ModelResponse response, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await OwnLeaseAsync(job, ct)) return;
        var currentProject = await db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\" = {job.Project.Id} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        var run = await db.AgentRuns.SingleAsync(r => r.Id == job.Request.AgentRunId, ct);
        if (!plan.NeedsClarification)
        {
            var task = new DevelopmentTask { ProjectId = job.Project.Id, Title = plan.Title, Description = plan.Description,
                AcceptanceCriteria = string.Join("\n", plan.AcceptanceCriteria.Select(x => "- " + x)), CreatedBy = job.Agent.Id };
            task.TransitionTo(Buildra.Domain.Tasks.TaskStatus.Ready);
            db.Tasks.Add(task); run.TaskId = task.Id;
            if (currentProject.AutoStartTasks && currentProject.RepositoryVerifiedAt is not null)
                db.ExecutionJobs.Add(new() { ProjectId = task.ProjectId, TaskId = task.Id });
            db.Conversations.Add(new() { OrganizationId = job.Project.OrganizationId, ProjectId = job.Project.Id, TaskId = task.Id, Type = ConversationType.Task });
            db.WorkflowEvents.Add(new() { OrganizationId = job.Project.OrganizationId, ProjectId = job.Project.Id, TaskId = task.Id, EventType = "TaskCreated" });
        }
        run.Status = AgentRunStatus.Completed; run.CompletedAt = DateTimeOffset.UtcNow;
        run.Model = response.Model; run.InputTokens = response.InputTokens; run.OutputTokens = response.OutputTokens;
        run.EstimatedCost = response.EstimatedCost;
        run.Result = response.Content;
        job.Request.Status = PlanningRequestStatus.Completed; job.Request.LeaseExpiresAt = null;
        db.Messages.Add(new() { ConversationId = job.Request.ConversationId, SenderId = job.Agent.Id, SenderType = SenderType.Agent,
            Content = plan.Summary, MessageType = plan.NeedsClarification ? "ActionRequired" : "Team", Metadata = JsonSerializer.Serialize(new { runId = run.Id, taskId = run.TaskId }) });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task FailAsync(PlanningJob job, string safeError, ModelResponse? response, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await OwnLeaseAsync(job, ct)) return;
        var run = await db.AgentRuns.SingleAsync(r => r.Id == job.Request.AgentRunId, ct);
        run.Status = AgentRunStatus.Failed; run.CompletedAt = DateTimeOffset.UtcNow; run.Error = safeError;
        if (response is not null) { run.Model = response.Model; run.InputTokens = response.InputTokens; run.OutputTokens = response.OutputTokens; run.EstimatedCost = response.EstimatedCost; }
        job.Request.Status = PlanningRequestStatus.Failed; job.Request.LeaseExpiresAt = null;
        db.Messages.Add(new() { ConversationId = job.Request.ConversationId, SenderType = SenderType.System, Content = safeError, MessageType = "ActionRequired" });
        db.WorkflowEvents.Add(new() { OrganizationId = job.Project.OrganizationId, ProjectId = job.Project.Id, EventType = "AgentRunFailed" });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    private async Task<bool> OwnLeaseAsync(PlanningJob job, CancellationToken ct)
    {
        // Lock and re-read: a recovered lease must never accept a stale worker's result.
        var current = await db.PlanningRequests.FromSqlInterpolated($"SELECT * FROM \"PlanningRequests\" WHERE \"Id\" = {job.Request.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        return current?.Status == PlanningRequestStatus.Running && current.LeaseToken == job.Request.LeaseToken;
    }
}
