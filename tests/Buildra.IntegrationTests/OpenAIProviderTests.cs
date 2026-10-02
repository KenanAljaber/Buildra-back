using System.Net;
using System.Text.Json;
using Buildra.Application.Models;
using Buildra.Infrastructure.Models;
using Microsoft.Extensions.Options;
namespace Buildra.IntegrationTests;

public sealed class OpenAIProviderTests
{
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
