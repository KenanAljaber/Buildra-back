using Buildra.Domain.Projects;
using Buildra.Domain.Agents;
namespace Buildra.Application.Projects;

public record ProjectInput(string Name, string Description, string RepositoryUrl, string DefaultBranch, string Instructions);
public record ProjectDetails(Project Project, IReadOnlyList<AgentDefinition> Team);
public interface IProjectStore
{
    Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, CancellationToken ct);
    Task<ProjectDetails?> GetAsync(Guid organizationId, Guid id, CancellationToken ct);
    Task AddAsync(Project project, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    Task DeleteAsync(Project project, CancellationToken ct);
}
public sealed class ProjectUseCases(IProjectStore store)
{
    public Task<IReadOnlyList<Project>> ListAsync(Guid org, CancellationToken ct) => store.ListAsync(org, ct);
    public Task<ProjectDetails?> GetAsync(Guid org, Guid id, CancellationToken ct) => store.GetAsync(org, id, ct);
    public async Task<Project> CreateAsync(Guid org, ProjectInput input, CancellationToken ct)
    {
        Validate(input);
        var project = new Project { OrganizationId = org };
        Apply(project, input);
        await store.AddAsync(project, ct);
        return project;
    }
    public async Task<Project?> UpdateAsync(Guid org, Guid id, ProjectInput input, CancellationToken ct)
    {
        Validate(input);
        var details = await store.GetAsync(org, id, ct);
        if (details is null) return null;
        Apply(details.Project, input);
        await store.SaveAsync(ct);
        return details.Project;
    }
    public async Task<bool> DeleteAsync(Guid org, Guid id, CancellationToken ct)
    {
        var details = await store.GetAsync(org, id, ct);
        if (details is null) return false;
        await store.DeleteAsync(details.Project, ct);
        return true;
    }
    public static void Validate(ProjectInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120)
            throw new ArgumentException("Project name must contain 1–120 characters.");
        if (input.Description is null || input.Description.Length > 2000 || input.Instructions is null || input.Instructions.Length > 20000)
            throw new ArgumentException("Description or instructions exceed their allowed length.");
        if (!Uri.TryCreate(input.RepositoryUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.Port != 443 ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$"))
            throw new ArgumentException("Use a GitHub repository URL such as https://github.com/owner/repository.");
        if (string.IsNullOrWhiteSpace(input.DefaultBranch) || input.DefaultBranch.Length > 200 || input.DefaultBranch.StartsWith('-') ||
            input.DefaultBranch.Contains("..") || input.DefaultBranch.Contains("@{") || input.DefaultBranch.EndsWith('/') ||
            input.DefaultBranch.EndsWith('.') || input.DefaultBranch.Split('/').Any(s => s.Length == 0 || s.StartsWith('.') || s.EndsWith(".lock")) ||
            input.DefaultBranch.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "~^:?*[\\".Contains(c)))
            throw new ArgumentException("Default branch must be a valid Git branch name.");
    }
    private static void Apply(Project p, ProjectInput i)
    {
        p.Name = i.Name.Trim(); p.Description = i.Description.Trim(); p.RepositoryUrl = i.RepositoryUrl.TrimEnd('/');
        p.DefaultBranch = i.DefaultBranch; p.Instructions = i.Instructions.Trim();
    }
}
