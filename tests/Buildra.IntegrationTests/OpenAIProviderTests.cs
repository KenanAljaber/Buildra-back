using System.Net;
using System.Text.Json;
using Buildra.Application.Models;
using Buildra.Infrastructure.Models;
using Microsoft.Extensions.Options;
namespace Buildra.IntegrationTests;

public sealed class OpenAIProviderTests
{
    [Theory]
    [InlineData(Buildra.Domain.Agents.AgentRole.Developer)]
    [InlineData(Buildra.Domain.Agents.AgentRole.Reviewer)]
    public async Task CodingUsesRoleScopedNativeToolsWithOnlyRelevantArguments(Buildra.Domain.Agents.AgentRole role)
    {
        using var http = new HttpClient(new Handler(async request => {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.False(root.TryGetProperty("text", out _));
            Assert.Equal("required", root.GetProperty("tool_choice").GetString());
            Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
            var tools = root.GetProperty("tools").EnumerateArray().ToArray();
            Assert.Equal(Buildra.Application.Execution.AgentAction.AllowedActions(role), tools.Select(t => t.GetProperty("name").GetString()));
            var read = tools.Single(t => t.GetProperty("name").GetString() == "readFile");
            Assert.Equal(new[] { "path" }, read.GetProperty("parameters").GetProperty("properties").EnumerateObject().Select(p => p.Name));
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"status":"completed","model":"test-model","output":[{"type":"function_call","name":"readFile","arguments":"{\"path\":\"package.json\"}"}],"usage":{"input_tokens":100,"output_tokens":20}}""") };
        }));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test" }));
        var result = await provider.GenerateAsync(new("StrongCoding", "Read", "Context", Buildra.Application.Execution.AgentAction.Schema(role), "agent_action"), default);
        var action = Buildra.Application.Execution.AgentAction.Parse(result.Content, role);
        Assert.Equal("readFile", action.Action); Assert.Equal("package.json", action.Path); Assert.Equal("", action.Content);
    }

    [Fact]
    public async Task TruncatedActionRecoversWithLargerBudgetAndCountsAllUsage()
    {
        var calls = 0; var recoveries = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(calls++ == 0 ? 8000 : 16000, body.RootElement.GetProperty("max_output_tokens").GetInt32());
            return new(HttpStatusCode.OK) { Content = new StringContent(calls == 1
                ? """{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"model":"test-model","usage":{"input_tokens":100,"output_tokens":8000}}"""
                : """{"status":"completed","model":"test-model","output":[{"type":"message","content":[{"type":"output_text","text":"{}"}]}],"usage":{"input_tokens":120,"output_tokens":50}}""") };
        }));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test", InputCostPerMillionTokens = 1, OutputCostPerMillionTokens = 2 }));
        var result = await provider.GenerateAsync(new("StrongCoding", "Write", "Context", MaxOutputTokens: 8000, OnRecovery: message => { recoveries.Add(message); return Task.CompletedTask; }), default);
        Assert.Equal(2, calls); Assert.Single(recoveries); Assert.Equal(220, result.InputTokens); Assert.Equal(8050, result.OutputTokens); Assert.Equal(0.01632m, result.EstimatedCost);
    }

    [Fact]
    public async Task AutomaticRecoveryIsBoundedAndPreservesFailedUsage()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"model":"test","usage":{"input_tokens":10,"output_tokens":20}}""") }); }));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test" }));
        var failure = await Assert.ThrowsAsync<ModelProviderException>(() => provider.GenerateAsync(new("StrongCoding", "", ""), default));
        Assert.Equal(3, calls); Assert.False(failure.Retryable); Assert.Equal(30, failure.Usage!.InputTokens); Assert.Equal(60, failure.Usage.OutputTokens);
    }

    [Fact]
    public async Task QuotaErrorsDoNotTriggerAutomaticPaidRetries()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{"error":{"code":"insufficient_quota"}}""") }); }));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test" }));
        await Assert.ThrowsAsync<ModelProviderException>(() => provider.GenerateAsync(new("StrongCoding", "", ""), default)); Assert.Equal(1, calls);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
    [Fact]
    public async Task SendsStrictSchemaAndExtractsOnlyTextAndUsage()
    {
        using var http = new HttpClient(new Handler(async request => {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"status":"completed","model":"test-model","output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"{\"summary\":\"Planned\"}"}]}],"usage":{"input_tokens":100,"output_tokens":50}}""") };
        }));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test-key", InputCostPerMillionTokens = 1m, OutputCostPerMillionTokens = 2m }));
        var response = await provider.GenerateAsync(new("StrongReasoning", "Plan", "Request"), default);
        Assert.Equal("{\"summary\":\"Planned\"}", response.Content); Assert.Equal(100, response.InputTokens); Assert.Equal(0.0002m, response.EstimatedCost);
    }
    [Fact]
    public async Task ProviderFailureDoesNotExposeErrorBody()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive-provider-detail") })));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions { ApiKey = "test-key" }));
        var error = await Assert.ThrowsAsync<ModelProviderException>(() => provider.GenerateAsync(new("StrongReasoning", "", ""), default));
        Assert.DoesNotContain("sensitive-provider-detail", error.Message); Assert.Contains("401", error.Message);
    }
    [Fact]
    public async Task MissingKeyFailsBeforeMakingNetworkCall()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("No request expected")));
        var provider = new OpenAIModelProvider(http, Options.Create(new OpenAIOptions()));
        await Assert.ThrowsAsync<ModelProviderException>(() => provider.GenerateAsync(new("StrongReasoning", "", ""), default));
    }
}
