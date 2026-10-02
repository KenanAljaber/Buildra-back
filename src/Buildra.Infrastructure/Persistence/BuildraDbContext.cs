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
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<ProjectAgent>().HasKey(x => new { x.ProjectId, x.AgentDefinitionId });
        m.Entity<Project>().Property(x => x.Name).HasMaxLength(120);
        m.Entity<Project>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        m.Entity<Project>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<User>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<AgentDefinition>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        m.Entity<ProjectAgent>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<ProjectAgent>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.AgentDefinitionId);
        m.Entity<DevelopmentTask>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<AgentRun>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<AgentRun>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Review>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<Review>().HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.ReviewerAgentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Conversation>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<Conversation>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Message>().HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId);
        m.Entity<Message>().HasIndex(x => new { x.ConversationId, x.CreatedAt });
        m.Entity<WorkflowEvent>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId);
        m.Entity<WorkflowEvent>().HasIndex(x => new { x.ProjectId, x.OccurredAt });
        m.Entity<Decision>().HasOne<DevelopmentTask>().WithMany().HasForeignKey(x => x.TaskId);
        m.Entity<AgentRun>().Property(x => x.EstimatedCost).HasPrecision(18, 8);
    }
}
