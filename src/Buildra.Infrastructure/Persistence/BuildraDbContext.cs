using Buildra.Domain.Agents;
using Buildra.Domain.Conversations;
using Buildra.Domain.Identity;
using Buildra.Domain.Projects;
using Buildra.Domain.Tasks;
using Buildra.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
namespace Buildra.Infrastructure.Persistence;

public sealed class BuildraDbContext(DbContextOptions<BuildraDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AgentDefinition> Agents => Set<AgentDefinition>();
    public DbSet<ProjectAgent> ProjectAgents => Set<ProjectAgent>();
    public DbSet<DevelopmentTask> Tasks => Set<DevelopmentTask>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<PlanningRequest> PlanningRequests => Set<PlanningRequest>();
    public DbSet<ExecutionJob> ExecutionJobs => Set<ExecutionJob>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<ProjectAgent>().HasKey(x => new { x.ProjectId, x.AgentDefinitionId });
        m.Entity<ExecutionJob>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<ExecutionJob>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<ExecutionJob>().HasIndex(x => x.TaskId).IsUnique();
        m.Entity<ExecutionJob>().HasIndex(x => new { x.Status, x.CreatedAt });
        m.Entity<ToolExecution>().HasOne<AgentRun>().WithMany().HasForeignKey(x => x.AgentRunId);
        m.Entity<Project>().Property(x => x.Name).HasMaxLength(120);
        m.Entity<Project>().Property(x => x.TestImage).HasDefaultValue("node:24-alpine");
        m.Entity<Project>().Property(x => x.TestCommand).HasDefaultValue("node --test");
        m.Entity<Project>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        m.Entity<Project>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<User>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<AgentDefinition>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<ProjectAgent>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<ProjectAgent>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.AgentDefinitionId);
        m.Entity<DevelopmentTask>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<AgentRun>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<AgentRun>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<PlanningRequest>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<PlanningRequest>().HasOne<AgentRun>().WithMany().HasForeignKey(x => x.AgentRunId);
        m.Entity<PlanningRequest>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId);
        m.Entity<PlanningRequest>().HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId);
        m.Entity<PlanningRequest>().HasIndex(x => x.AgentRunId).IsUnique();
        m.Entity<PlanningRequest>().HasIndex(x => new { x.Status, x.CreatedAt });
        m.Entity<AgentRun>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Review>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<Review>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.ReviewerAgentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Conversation>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<Conversation>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<Message>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId);
        m.Entity<Message>().HasIndex(x => new { x.ConversationId, x.CreatedAt });
        m.Entity<WorkflowEvent>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<WorkflowEvent>().HasIndex(x => new { x.ProjectId, x.OccurredAt });
        m.Entity<Decision>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<AgentRun>().Property(x => x.EstimatedCost).HasPrecision(18, 8);
    }
}
