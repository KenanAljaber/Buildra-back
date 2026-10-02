using Buildra.Application.Execution;
using Buildra.Domain.Agents;
namespace Buildra.UnitTests;
public sealed class ExecutionPermissionTests
{
    [Theory]
    [InlineData(AgentRole.Reviewer)]
    [InlineData(AgentRole.ProductManager)]
    public void NonDeveloperCannotWriteEvenWithMisconfiguredPermission(AgentRole role)
    {
        var agent = new AgentDefinition { Role = role, Permissions = AgentPermission.WriteRepository };
        Assert.Throws<ExecutionException>(() => ExecuteCodeWorkflow.Require(agent, AgentPermission.WriteRepository));
    }
    [Fact]
    public void ReviewerCannotReturnWriteAction() => Assert.Throws<ExecutionException>(() => AgentAction.Parse("""{"action":"writeFile","path":"app.js","content":"bad","query":"","summary":""}""", AgentRole.Reviewer));
    [Fact]
    public void ReviewerSchemaHasNoWriteTool()
    {
        Assert.DoesNotContain("writeFile", AgentAction.Schema(AgentRole.Reviewer)); Assert.DoesNotContain("deleteFile", AgentAction.Schema(AgentRole.Reviewer));
    }
}
