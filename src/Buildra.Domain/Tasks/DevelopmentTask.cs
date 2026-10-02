namespace Buildra.Domain.Tasks;

public enum TaskStatus { Draft, Ready, InDevelopment, InReview, ChangesRequested, Approved, Completed, Blocked, Failed }

public sealed class DevelopmentTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public TaskStatus Status { get; private set; } = TaskStatus.Draft;
    public int Priority { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? PullRequestUrl { get; set; }

    public void TransitionTo(TaskStatus next)
    {
        var allowed = Status switch
        {
            TaskStatus.Draft => next == TaskStatus.Ready,
            TaskStatus.Ready => next == TaskStatus.InDevelopment,
            TaskStatus.InDevelopment => next == TaskStatus.InReview,
            TaskStatus.InReview => next is TaskStatus.ChangesRequested or TaskStatus.Approved,
            TaskStatus.ChangesRequested => next == TaskStatus.InDevelopment,
            TaskStatus.Approved => next == TaskStatus.Completed,
            TaskStatus.Blocked => next == TaskStatus.Ready,
            _ => false
        };
        if (Status is not (TaskStatus.Completed or TaskStatus.Failed) && next is TaskStatus.Blocked or TaskStatus.Failed)
            allowed = true;
        if (!allowed) throw new InvalidOperationException($"Cannot transition from {Status} to {next}.");
        if (next == TaskStatus.Ready && string.IsNullOrWhiteSpace(AcceptanceCriteria))
            throw new InvalidOperationException("Acceptance criteria are required before development.");
        if (next == TaskStatus.Completed && string.IsNullOrWhiteSpace(PullRequestUrl))
            throw new InvalidOperationException("A pull request is required to complete a task.");
        Status = next;
    }
}

public enum ReviewStatus { Approved, ChangesRequested, Rejected }
public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid ReviewerAgentId { get; set; }
    public ReviewStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public string Findings { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
