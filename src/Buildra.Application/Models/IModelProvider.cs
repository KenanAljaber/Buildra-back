namespace Buildra.Application.Models;
public record ModelRequest(string Profile, string Instructions, string Context);
public record ModelResponse(string Content, string Model, int InputTokens, int OutputTokens);
public interface IModelProvider
{
    Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}
