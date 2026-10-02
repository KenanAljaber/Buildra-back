using System.Text.Json;
namespace Buildra.Application.Planning;

public sealed record TaskPlan(string Title, string Description, string[] AcceptanceCriteria, string Summary, bool NeedsClarification)
{
    public static TaskPlan Parse(string json)
    {
        var plan = JsonSerializer.Deserialize<TaskPlan>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The PM returned an empty plan.");
        if (string.IsNullOrWhiteSpace(plan.Summary) || plan.Summary.Length > 4000)
            throw new InvalidOperationException("The PM returned an invalid summary.");
        if (!plan.NeedsClarification && (string.IsNullOrWhiteSpace(plan.Title) || plan.Title.Length > 200 ||
            string.IsNullOrWhiteSpace(plan.Description) || plan.Description.Length > 12000 || plan.AcceptanceCriteria is null ||
            plan.AcceptanceCriteria.Length is < 1 or > 15 || plan.AcceptanceCriteria.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 2000)))
            throw new InvalidOperationException("The PM plan must include a title, description, and measurable acceptance criteria.");
        return plan;
    }
}
