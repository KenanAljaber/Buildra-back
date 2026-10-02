namespace Buildra.Domain.Workflows;
public enum ExecutionJobStatus { Queued, Running, Completed, Failed }
public sealed class ExecutionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid ProjectId { get; set; }
    public ExecutionJobStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? Error { get; set; }
}
