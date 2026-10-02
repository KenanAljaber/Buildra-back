namespace Buildra.Application.Models;
public record ModelRequest(string Profile, string Instructions, string Context, string? OutputSchema = null, string OutputName = "task_plan", int MaxOutputTokens = 3000);
public record ModelResponse(string Content, string Model, int InputTokens, int OutputTokens, decimal? EstimatedCost = null);
public interface IModelProvider
{
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}
public sealed class ModelProviderException(string safeMessage) : Exception(safeMessage);
