namespace Buildra.Domain.Agents;
public sealed class ToolExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentRunId { get; set; }
    public string Tool { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Succeeded { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
