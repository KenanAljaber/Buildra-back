using System.Text.Json;
using Buildra.Domain.Agents;
namespace Buildra.Application.Execution;

public record AgentAction(string Action, string Path, string Content, string Query, string Summary)
{
    public static AgentAction Parse(string json, AgentRole role)
    {
        var action = JsonSerializer.Deserialize<AgentAction>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ExecutionException("The agent returned no action.");
        if (action.Action is null || !AllowedActions(role).Contains(action.Action) || action.Path is null || action.Content is null || action.Query is null ||
            action.Summary is null || action.Summary.Length > 8000) throw new ExecutionException("The agent returned an invalid or forbidden action.");
        return action;
    }
    public static string[] AllowedActions(AgentRole role) => role switch {
        AgentRole.Developer => ["readFile", "searchFiles", "writeFile", "deleteFile", "runTests", "complete"],
        AgentRole.Reviewer => ["readFile", "searchFiles", "runTests", "approve", "requestChanges"],
        _ => []
    };
    public static string Schema(AgentRole role) => JsonSerializer.Serialize(new {
        type = "object", properties = new {
            action = new { type = "string", @enum = AllowedActions(role) }, path = new { type = "string" }, content = new { type = "string" },
            query = new { type = "string" }, summary = new { type = "string" }
        }, required = new[] { "action", "path", "content", "query", "summary" }, additionalProperties = false
    });
}
