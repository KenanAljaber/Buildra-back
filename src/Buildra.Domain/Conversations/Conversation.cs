namespace Buildra.Domain.Conversations;

public enum ConversationType { Project, Direct, Task }
public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? AgentDefinitionId { get; set; }
    public ConversationType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SenderType { User, Agent, System }
public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public SenderType SenderType { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = "";
    public string MessageType { get; set; } = "User";
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
