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
        var previous = new List<ModelResponse>();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await SendOnceAsync(request, cancellationToken);
                return result with { InputTokens = result.InputTokens + previous.Sum(r => r.InputTokens), OutputTokens = result.OutputTokens + previous.Sum(r => r.OutputTokens),
                    EstimatedCost = result.EstimatedCost is null || previous.Any(r => r.EstimatedCost is null) ? null : result.EstimatedCost + previous.Sum(r => r.EstimatedCost) };
            }
            catch (ModelProviderException error)
            {
                if (error.Usage is not null) previous.Add(error.Usage);
                if (!error.Retryable || attempt >= 2)
                {
                    var usage = previous.Count == 0 ? null : previous[^1] with { Content = "", InputTokens = previous.Sum(r => r.InputTokens), OutputTokens = previous.Sum(r => r.OutputTokens),
                        EstimatedCost = previous.Any(r => r.EstimatedCost is null) ? null : previous.Sum(r => r.EstimatedCost) };
                    throw new ModelProviderException(error.Message, false, error.Code, usage);
                }
                if (error.Code == "max_output_tokens") request = request with { MaxOutputTokens = Math.Min(request.MaxOutputTokens * 2, 16000),
                    Instructions = request.Instructions + "\nYour previous action exceeded its output budget. Produce one small action; split implementation into multiple small files." };
                if (request.OnRecovery is not null) await request.OnRecovery($"Recovering automatically ({attempt + 1}/2): {error.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken);
            }
        }
    }
    private async Task<ModelResponse> SendOnceAsync(ModelRequest request, CancellationToken cancellationToken)
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
        HttpResponseMessage sent;
        try { sent = await http.SendAsync(message, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ModelProviderException("The model request timed out.", true); }
        catch (HttpRequestException) { throw new ModelProviderException("The model connection was interrupted.", true); }
        using var response = sent;
        if (!response.IsSuccessStatusCode)
        {
            var retryable = (int)response.StatusCode is 408 or 429 or >= 500;
            if ((int)response.StatusCode == 429)
            {
                try { using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (failure.RootElement.GetProperty("error").GetProperty("code").GetString() == "insufficient_quota") retryable = false;
                } catch (JsonException) { } catch (KeyNotFoundException) { }
            }
            throw new ModelProviderException($"OpenAI request failed (HTTP {(int)response.StatusCode}). Check the worker's API key, model access, and account limits.", retryable);
        }
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = body.RootElement;
        ModelResponse? usageResponse = null;
        if (root.TryGetProperty("usage", out var returnedUsage) && returnedUsage.ValueKind == JsonValueKind.Object)
            usageResponse = new("", root.GetProperty("model").GetString()!, returnedUsage.GetProperty("input_tokens").GetInt32(), returnedUsage.GetProperty("output_tokens").GetInt32(), Cost(returnedUsage.GetProperty("input_tokens").GetInt32(), returnedUsage.GetProperty("output_tokens").GetInt32(), settings));
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
        {
            var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object && details.TryGetProperty("reason", out var cause) ? cause.GetString() : "unknown";
            throw new ModelProviderException(reason == "max_output_tokens" ? "The generated action exceeded its output limit." : "The model could not finish this action. Inspect the request before retrying.", reason == "max_output_tokens", reason ?? "unknown", usageResponse);
        }
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
    private static decimal? Cost(int input, int output, OpenAIOptions settings) => settings.InputCostPerMillionTokens is >= 0 && settings.OutputCostPerMillionTokens is >= 0
        ? (input * settings.InputCostPerMillionTokens + output * settings.OutputCostPerMillionTokens) / 1_000_000m : null;
}
