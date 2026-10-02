using Buildra.Application.Projects;
using Buildra.Domain.Agents;
using Buildra.Domain.Conversations;
using Buildra.Domain.Projects;
using Buildra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Buildra.Infrastructure.Projects;

public sealed class EfProjectStore(BuildraDbContext db) : IProjectStore
{
    public async Task<IReadOnlyList<Project>> ListAsync(Guid org, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(x => x.OrganizationId == org).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public async Task<ProjectDetails?> GetAsync(Guid org, Guid id, CancellationToken ct)
    {
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org, ct);
        if (project is null) return null;
        var team = await (from assignment in db.ProjectAgents join agent in db.Agents on assignment.AgentDefinitionId equals agent.Id
            where assignment.ProjectId == id && assignment.Enabled select agent).AsNoTracking().ToListAsync(ct);
        return new(project, team);
    }
    public async Task AddAsync(Project project, CancellationToken ct)
    {
        db.Projects.Add(project);
        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var agent = AgentDefinition.Template(project.OrganizationId, role);
            db.Agents.Add(agent);
            db.ProjectAgents.Add(new() { ProjectId = project.Id, AgentDefinitionId = agent.Id });
        }
        db.Conversations.Add(new() { OrganizationId = project.OrganizationId, ProjectId = project.Id, Type = ConversationType.Project });
        db.WorkflowEvents.Add(new() { OrganizationId = project.OrganizationId, ProjectId = project.Id, EventType = "ProjectCreated" });
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
    public async Task DeleteAsync(Project project, CancellationToken ct)
    {
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
    }
}
