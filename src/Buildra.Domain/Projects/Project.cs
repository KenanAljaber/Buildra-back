namespace Buildra.Domain.Projects;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public string Instructions { get; set; } = "";
    public DateTimeOffset? RepositoryVerifiedAt { get; set; }
    public long? GitHubRepositoryId { get; set; }
    public string TestImage { get; set; } = "node:24-alpine";
    public string TestCommand { get; set; } = "node --test";
    public bool AutoStartTasks { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
