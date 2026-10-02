using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buildra.Application.Models;
using Microsoft.Extensions.Options;
namespace Buildra.Infrastructure.Models;

public sealed class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string StrongReasoningModel { get; set; } = "gpt-4.1-mini";
    public string StrongCodingModel { get; set; } = "gpt-4.1-mini";
    public decimal? InputCostPerMillionTokens { get; set; }
    public decimal? OutputCostPerMillionTokens { get; set; }
}

public sealed class OpenAIModelProvider(HttpClient http, IOptions<OpenAIOptions> options) : IModelProvider
{
    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new ModelProviderException("OpenAI is not configured. Set OpenAI:ApiKey in the worker's local user secrets, then retry.");
        var model = request.Profile switch { "StrongReasoning" => settings.StrongReasoningModel, "StrongCoding" => settings.StrongCodingModel, _ => throw new ModelProviderException("The requested model profile is not configured.") };
        var schema = JsonSerializer.Deserialize<JsonElement>(request.OutputSchema ?? """
            {"type":"object","properties":{"title":{"type":"string"},"description":{"type":"string"},
              "acceptanceCriteria":{"type":"array","items":{"type":"string"}},"summary":{"type":"string"},"needsClarification":{"type":"boolean"}},
             "required":["title","description","acceptanceCriteria","summary","needsClarification"],"additionalProperties":false}
            """);
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Content = JsonContent.Create(new { model, instructions = request.Instructions,
            input = request.Context, store = false, max_output_tokens = request.MaxOutputTokens,
            text = new { format = new { type = "json_schema", name = request.OutputName, strict = true, schema } } });
        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new ModelProviderException($"OpenAI request failed (HTTP {(int)response.StatusCode}). Check the worker's API key, model access, and account limits, then retry.");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = body.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new ModelProviderException("OpenAI did not finish the plan. Please retry with a smaller request.");
        var content = new List<string>();
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "message") continue;
            foreach (var part in item.GetProperty("content").EnumerateArray())
            {
                if (part.GetProperty("type").GetString() == "refusal") throw new ModelProviderException("OpenAI declined this request. Rephrase the product requirements and try again.");
                if (part.GetProperty("type").GetString() == "output_text") content.Add(part.GetProperty("text").GetString()!);
            }
        }
        if (content.Count == 0) throw new ModelProviderException("OpenAI returned no task plan. Please retry.");
        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();
        decimal? cost = settings.InputCostPerMillionTokens is >= 0 && settings.OutputCostPerMillionTokens is >= 0
            ? (inputTokens * settings.InputCostPerMillionTokens + outputTokens * settings.OutputCostPerMillionTokens) / 1_000_000m : null;
        return new(string.Concat(content), root.GetProperty("model").GetString()!, inputTokens, outputTokens, cost);
    }
}
