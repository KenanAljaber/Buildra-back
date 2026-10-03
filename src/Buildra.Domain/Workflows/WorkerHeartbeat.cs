namespace Buildra.Domain.Workflows;
public sealed class WorkerHeartbeat
{
    public string Id { get; set; } = "agents";
    public DateTimeOffset UpdatedAt { get; set; }
}
