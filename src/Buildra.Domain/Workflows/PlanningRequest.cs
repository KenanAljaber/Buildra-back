namespace Buildra.Domain.Workflows;

public enum PlanningRequestStatus { Queued, Running, Completed, Failed }
public sealed class PlanningRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public PlanningRequestStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
