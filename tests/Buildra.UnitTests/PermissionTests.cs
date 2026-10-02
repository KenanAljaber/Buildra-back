using Buildra.Domain.Agents;
using Buildra.Application.Projects;
namespace Buildra.UnitTests;

public sealed class PermissionTests
{
    [Theory]
    [InlineData(AgentRole.ProductManager)]
    [InlineData(AgentRole.Reviewer)]
    public void NonDevelopersCannotWriteOrCommit(AgentRole role)
    {
        var agent = AgentDefinition.Template(Guid.NewGuid(), role);
        Assert.False(agent.Allows(AgentPermission.WriteRepository));
        Assert.False(agent.Allows(AgentPermission.Commit));
        Assert.False(agent.Allows(AgentPermission.Push));
        Assert.True(agent.Allows(AgentPermission.ReadRepository));
    }
    [Fact]
    public void DeveloperCanImplementButCannotSubmitReview()
    {
        var agent = AgentDefinition.Template(Guid.NewGuid(), AgentRole.Developer);
        Assert.True(agent.Allows(AgentPermission.WriteRepository | AgentPermission.Commit));
        Assert.False(agent.Allows(AgentPermission.SubmitReview));
    }
    [Theory]
    [InlineData("https://github.com.evil.test/owner/repo", "main")]
    [InlineData("https://token@github.com/owner/repo", "main")]
    [InlineData("https://github.com/owner/repo", "--upload-pack=evil")]
    [InlineData("https://github.com/owner/repo", "../escape")]
    [InlineData("https://github.com/owner/repo", "main; command")]
    public void RejectsInvalidRepositoryAndBranch(string url, string branch) =>
        Assert.Throws<ArgumentException>(() => ProjectUseCases.Validate(new("Project", "", url, branch, "")));
    [Fact]
    public void AcceptsGitHubFeatureBranch() => ProjectUseCases.Validate(new("Project", "", "https://github.com/owner/repo", "feature/dashboard", ""));
}
