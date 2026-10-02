namespace Buildra.Domain.Agents;

public enum AgentRole { ProductManager, Developer, Reviewer }
[Flags]
public enum AgentPermission { None = 0, ReadRepository = 1, WriteRepository = 2, RunTests = 4, Commit = 8, Push = 16, CreateTask = 32, SubmitReview = 64 }

public sealed class AgentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public AgentRole Role { get; set; }
    public string Instructions { get; set; } = "";
    public string ModelProfile { get; set; } = "StrongReasoning";
    public AgentPermission Permissions { get; set; }
    public int MaxToolCalls { get; set; } = 40;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Allows(AgentPermission permission) => permission != AgentPermission.None && (Permissions & permission) == permission;

    public static AgentDefinition Template(Guid organizationId, AgentRole role) => new()
    {
        OrganizationId = organizationId, Role = role,
        Name = role switch { AgentRole.ProductManager => "Sarah", AgentRole.Developer => "Alex", _ => "Daniel" },
        ModelProfile = role == AgentRole.Developer ? "StrongCoding" : "StrongReasoning",
        Instructions = role switch
        {
            AgentRole.ProductManager => "Define bounded tasks and measurable acceptance criteria. Escalate product ambiguity.",
            AgentRole.Developer => "Implement the assigned task, validate changes, and commit in an isolated workspace.",
            _ => "Review the diff against acceptance criteria. Report findings; do not modify code."
        },
        Permissions = role switch
        {
            AgentRole.ProductManager => AgentPermission.ReadRepository | AgentPermission.CreateTask,
            AgentRole.Developer => AgentPermission.ReadRepository | AgentPermission.WriteRepository | AgentPermission.RunTests | AgentPermission.Commit | AgentPermission.Push,
            _ => AgentPermission.ReadRepository | AgentPermission.RunTests | AgentPermission.SubmitReview
        }
    };
}

public sealed class ProjectAgent
{
    public Guid ProjectId { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public bool Enabled { get; set; } = true;
}

public enum AgentRunStatus { Queued, Running, WaitingForTool, Completed, Failed, Cancelled }
public sealed class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentDefinitionId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public AgentRunStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
