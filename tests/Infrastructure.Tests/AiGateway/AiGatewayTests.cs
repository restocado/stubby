using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Infrastructure.AiGateway;

namespace Stubby.Infrastructure.Tests.AiGateway;

public class AiGatewayTests
{
    private static readonly byte[] ImageBytes = [1, 2, 3, 4, 5];

    private static AiRequest Request(string prompt = "Read the receipt.", int images = 1) =>
        new(prompt, Enumerable.Repeat<ReadOnlyMemory<byte>>(ImageBytes, images).ToList());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provider_reply_is_returned_as_text()
    {
        await using var harness = GatewayHarness.Create();
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Reply("hello there"));

        var text = await harness.Gateway.CompleteAsync(Request(), Token);

        Assert.Equal("hello there", text);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Request_goes_to_the_chat_endpoint_over_https_with_the_key_prompt_and_images()
    {
        await using var harness = GatewayHarness.Create();

        await harness.Gateway.CompleteAsync(Request("Describe this.", images: 2), Token);

        Assert.Equal("https://ollama.test/api/chat", harness.Handler.LastUri?.ToString());
        Assert.Equal($"Bearer {GatewayHarness.ApiKey}", harness.Handler.LastAuthorization);
        using var body = JsonDocument.Parse(harness.Handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("gemma4:31b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        var message = root.GetProperty("messages")[0];
        Assert.Equal("Describe this.", message.GetProperty("content").GetString());
        Assert.Equal(2, message.GetProperty("images").GetArrayLength());
        Assert.Equal(Convert.ToBase64String(ImageBytes), message.GetProperty("images")[0].GetString());
    }

    [Fact]
    public async Task Changing_the_model_setting_changes_the_model_sent()
    {
        await using var harness = GatewayHarness.Create(("Model", "other-model:8b"));

        await harness.Gateway.CompleteAsync(Request(), Token);

        using var body = JsonDocument.Parse(harness.Handler.LastBody!);
        Assert.Equal("other-model:8b", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Response_slower_than_the_timeout_fails_and_cancels_the_http_call()
    {
        await using var harness = GatewayHarness.Create(("TimeoutBaseSeconds", "1"));
        harness.Handler.Respond = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeProviderHandler.Reply("never");
        };

        var timer = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));

        Assert.Contains("timed out", failure.Message, StringComparison.Ordinal);
        Assert.InRange(timer.Elapsed, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(5));
        Assert.True(harness.Handler.SawCancellation);
    }

    [Fact]
    public void Timeout_grows_with_the_number_of_images()
    {
        var options = new AiGatewayOptions { TimeoutBaseSeconds = 30, TimeoutPerImageSeconds = 15 };

        Assert.Equal(TimeSpan.FromSeconds(45), options.TimeoutFor(1));
        Assert.Equal(TimeSpan.FromSeconds(105), options.TimeoutFor(5));
    }

    [Fact]
    public async Task Provider_error_fails_with_the_provider_called_exactly_once()
    {
        await using var harness = GatewayHarness.Create();
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Status(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));

        Assert.Contains("HTTP 500", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Rejected_key_fails_without_opening_the_circuit()
    {
        await using var harness = GatewayHarness.Create(("CircuitFailureThreshold", "2"));
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Status(HttpStatusCode.Unauthorized));

        for (var i = 0; i < 4; i++)
        {
            var failure = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));
            Assert.Contains("HTTP 401", failure.Message, StringComparison.Ordinal);
        }

        Assert.Equal(4, harness.Handler.Calls);
    }

    [Fact]
    public async Task Circuit_opens_after_the_configured_failures_and_lets_one_call_through_after_the_break()
    {
        await using var harness = GatewayHarness.Create(("CircuitFailureThreshold", "2"), ("CircuitBreakSeconds", "1"));
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Status(HttpStatusCode.BadGateway));

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));
        }

        Assert.Equal(2, harness.Handler.Calls);

        var open = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));
        Assert.Contains("circuit is open", open.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.Handler.Calls);

        await Task.Delay(TimeSpan.FromMilliseconds(1300), Token);
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Reply("back"));

        Assert.Equal("back", await harness.Gateway.CompleteAsync(Request(), Token));
        Assert.Equal(3, harness.Handler.Calls);
    }

    [Fact]
    public async Task Call_over_the_rate_limit_waits_for_a_free_slot()
    {
        await using var harness = GatewayHarness.Create(("RateLimitBurst", "1"), ("RateLimitPerMinute", "120"));

        var timer = Stopwatch.StartNew();
        await harness.Gateway.CompleteAsync(Request(), Token);
        var afterFirst = timer.Elapsed;
        await harness.Gateway.CompleteAsync(Request(), Token);

        Assert.True(afterFirst < TimeSpan.FromMilliseconds(300), $"The first call took {afterFirst}.");
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(400), $"Both calls took only {timer.Elapsed}.");
        Assert.Equal(2, harness.Handler.Calls);
    }

    [Fact]
    public async Task Wait_for_a_rate_limit_slot_ends_at_the_timeout()
    {
        await using var harness = GatewayHarness.Create(
            ("RateLimitBurst", "1"), ("RateLimitPerMinute", "1"), ("TimeoutBaseSeconds", "1"));

        await harness.Gateway.CompleteAsync(Request(), Token);
        var failure = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));

        Assert.Contains("timed out", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Missing_api_key_fails_without_calling_the_provider()
    {
        await using var harness = GatewayHarness.Create(("OllamaCloud:ApiKey", ""));

        var failure = await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));

        Assert.Contains("API key", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task Unreadable_reply_fails()
    {
        await using var harness = GatewayHarness.Create();
        harness.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });

        await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(), Token));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_a_gateway_failure()
    {
        await using var harness = GatewayHarness.Create();
        harness.Handler.Respond = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeProviderHandler.Reply("never");
        };
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Gateway.CompleteAsync(Request(), cancel.Token));
    }

    [Fact]
    public async Task No_log_line_holds_the_images_prompt_reply_or_api_key()
    {
        await using var harness = GatewayHarness.Create(("CircuitFailureThreshold", "2"));
        const string prompt = "SECRET-PROMPT-TEXT";
        const string reply = "SECRET-REPLY-TEXT";
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Reply(reply));
        await harness.Gateway.CompleteAsync(Request(prompt), Token);
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Status(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<AiGatewayException>(() => harness.Gateway.CompleteAsync(Request(prompt), Token));

        var log = string.Join('\n', harness.Logs.Lines);

        Assert.Contains("AI call finished", log, StringComparison.Ordinal);
        Assert.Contains("tokens in 11 out 7", log, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt, log, StringComparison.Ordinal);
        Assert.DoesNotContain(reply, log, StringComparison.Ordinal);
        Assert.DoesNotContain(GatewayHarness.ApiKey, log, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(ImageBytes), log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://ollama.test")]
    [InlineData("ftp://ollama.test")]
    [InlineData("ollama.test")]
    public void Non_https_url_is_rejected_at_startup(string url)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(GatewayHarness.Settings(("OllamaCloud:BaseUrl", url)))
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAiGateway(configuration));

        Assert.Contains("https", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shipped_web_settings_are_valid_and_select_a_model_with_a_colon_in_its_name()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json"))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAiGateway(configuration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AiGatewayOptions>();
        Assert.Equal("gemma4:31b", options.Model);
        Assert.Equal("OllamaCloud", options.ProviderFor(options.Model));
    }

    [Fact]
    public void Model_that_is_not_mapped_to_a_provider_is_rejected_at_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(GatewayHarness.Settings(("Model", "unmapped-model")))
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAiGateway(configuration));
    }
}
