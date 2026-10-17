using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Infrastructure.AiGateway;

namespace Stubby.Infrastructure.Tests.AiGateway;

/// <summary>A fake model provider that sits where the network would be, below the real resilience pipeline.</summary>
internal sealed class FakeProviderHandler : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    public bool SawCancellation { get; private set; }

    public Uri? LastUri { get; private set; }

    public string? LastAuthorization { get; private set; }

    public string? LastBody { get; private set; }

    public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(Reply("ok"));

    public static HttpResponseMessage Reply(string text, int promptTokens = 11, int responseTokens = 7) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"message":{"role":"assistant","content":"{{text}}"},"prompt_eval_count":{{promptTokens}},"eval_count":{{responseTokens}}}""",
                Encoding.UTF8,
                "application/json"),
        };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("{}") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return await Respond(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SawCancellation = true;
            throw;
        }
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category} [{logLevel}] {formatter(state, exception)} {exception}");
    }
}

internal sealed class GatewayHarness : IAsyncDisposable
{
    public const string ApiKey = "test-api-key-5f3a9c";

    private readonly ServiceProvider _provider;

    private GatewayHarness(ServiceProvider provider, FakeProviderHandler handler, CapturingLoggerProvider logs)
    {
        _provider = provider;
        Handler = handler;
        Logs = logs;
    }

    public FakeProviderHandler Handler { get; }

    public CapturingLoggerProvider Logs { get; }

    public IAiGateway Gateway => _provider.GetRequiredService<IAiGateway>();

    public static Dictionary<string, string?> Settings(params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AiGateway:Model"] = "gemma4:31b",
            ["AiGateway:Models:0:Name"] = "gemma4:31b",
            ["AiGateway:Models:0:Provider"] = "OllamaCloud",
            ["AiGateway:Models:1:Name"] = "other-model:8b",
            ["AiGateway:Models:1:Provider"] = "OllamaCloud",
            ["AiGateway:OllamaCloud:BaseUrl"] = "https://ollama.test",
            ["AiGateway:OllamaCloud:ApiKey"] = ApiKey,
            ["AiGateway:TimeoutBaseSeconds"] = "30",
            ["AiGateway:TimeoutPerImageSeconds"] = "0",
        };
        foreach (var (key, value) in overrides)
        {
            settings[$"AiGateway:{key}"] = value;
        }

        return settings;
    }

    public static GatewayHarness Create(params (string Key, string Value)[] overrides)
    {
        var handler = new FakeProviderHandler();
        var logs = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings(overrides)).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddAiGateway(configuration);
        return new GatewayHarness(services.BuildServiceProvider(), handler, logs);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
