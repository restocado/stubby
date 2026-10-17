using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.AiGateway;

/// <summary>Talks to Ollama's native chat API. The resilience pipeline wraps the HttpClient it is given.</summary>
internal sealed class OllamaCloudAdapter(HttpClient http, AiGatewayOptions options) : IAiProviderAdapter
{
    /// <summary>Carries the timeout for this call to the pipeline's timeout strategy.</summary>
    internal static readonly HttpRequestOptionsKey<TimeSpan> TimeoutKey = new("Stubby.AiGateway.Timeout");

    public string Name => AiGatewayOptions.OllamaCloudProvider;

    public async Task<ProviderReply> SendAsync(
        string model,
        string prompt,
        IReadOnlyList<ReadOnlyMemory<byte>> images,
        CancellationToken cancellationToken)
    {
        var apiKey = options.OllamaCloud.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiGatewayException("The Ollama Cloud API key is not configured.");
        }

        var message = new ChatMessage("user", prompt, images.Select(i => Convert.ToBase64String(i.Span)).ToList());
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(new ChatRequest(model, [message], Stream: false)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Options.Set(TimeoutKey, options.TimeoutFor(images.Count));

        using var response = await http.SendAsync(request, cancellationToken);

        // The body is not read or kept: an error reply is not needed and might echo the request.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The provider returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var reply = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
        if (reply?.Message?.Content is not { } text)
        {
            throw new AiGatewayException("The provider's reply could not be read.");
        }

        return new ProviderReply(text, reply.PromptTokens, reply.ResponseTokens);
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("images")] IReadOnlyList<string> Images);

    private sealed record ChatResponse(
        [property: JsonPropertyName("message")] ReplyMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptTokens,
        [property: JsonPropertyName("eval_count")] int? ResponseTokens);

    private sealed record ReplyMessage([property: JsonPropertyName("content")] string? Content);
}
