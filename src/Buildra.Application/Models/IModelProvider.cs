namespace Buildra.Application.Models;
public record ModelRequest(string Profile, string Instructions, string Context, string? OutputSchema = null, string OutputName = "task_plan", int MaxOutputTokens = 3000, Func<string, Task>? OnRecovery = null);
public record ModelResponse(string Content, string Model, int InputTokens, int OutputTokens, decimal? EstimatedCost = null);
public interface IModelProvider
{
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}
public sealed class ModelProviderException(string safeMessage, bool retryable = false, string code = "", ModelResponse? usage = null) : Exception(safeMessage)
{
    public bool Retryable { get; } = retryable;
    public string Code { get; } = code;
    public ModelResponse? Usage { get; } = usage;
}
