using System.Net;
using System.Security.Cryptography;
using System.Text;
using Stubby.Application.Abstractions;
using Stubby.Infrastructure.AiGateway;
using Stubby.Infrastructure.Extraction;
using Stubby.Infrastructure.Tests.AiGateway;

namespace Stubby.Infrastructure.Tests.Extraction;

internal sealed class FakeAiGateway : IAiGateway
{
    public string Model => "fake-model:1b";

    public List<AiRequest> Requests { get; } = [];

    public Func<string> Reply { get; set; } = () => """{"merchant": "Cafe", "date": "2026-10-02", "total": 12.50}""";

    public Exception? Failure { get; set; }

    public Task<string> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Failure is null ? Task.FromResult(Reply()) : Task.FromException<string>(Failure);
    }
}

internal sealed class FakePdfRenderer : IPdfRenderer
{
    public List<byte[]> Pages { get; set; } = [[1], [2], [3]];

    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        Calls++;
        return Failure is null ? Task.FromResult<IReadOnlyList<byte[]>>(Pages) : Task.FromException<IReadOnlyList<byte[]>>(Failure);
    }
}

public class VisionReceiptExtractorTests
{
    // If this fails, the prompt text changed. Change ExtractionPrompt.Version and docs/extraction-prompt.md with it,
    // then update this hash.
    private const string PinnedPromptVersion = "v1";
    private const string PinnedPromptHash = "b29aa0fa698950611a30faa4f6dc60ebccc371847fc3eaf6a7603d62865ab00b";

    private readonly FakeAiGateway _gateway = new();
    private readonly FakePdfRenderer _renderer = new();
    private readonly VisionReceiptExtractor _extractor;

    public VisionReceiptExtractorTests()
    {
        _extractor = new VisionReceiptExtractor(_gateway, _renderer);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task Photo_is_sent_as_one_image(string contentType)
    {
        byte[] content = [9, 8, 7];

        await _extractor.ExtractAsync(new ReceiptFile(content, contentType), Token);

        var request = Assert.Single(_gateway.Requests);
        Assert.Equal([content], request.Images.Select(i => i.ToArray()));
        Assert.Equal(0, _renderer.Calls);
    }

    [Fact]
    public async Task Three_page_pdf_is_sent_as_three_images_in_page_order_in_one_call()
    {
        await _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "application/pdf"), Token);

        var request = Assert.Single(_gateway.Requests);
        Assert.Equal([[1], [2], [3]], request.Images.Select(i => i.ToArray()));
        Assert.Equal(1, _renderer.Calls);
    }

    [Fact]
    public async Task Request_carries_the_prompt()
    {
        await _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token);

        Assert.Equal(ExtractionPrompt.Text, Assert.Single(_gateway.Requests).Prompt);
    }

    [Fact]
    public async Task Full_reply_gives_merchant_date_total_subtotal_and_tax()
    {
        _gateway.Reply = () =>
            """{"merchant": "Lantern Noodle House", "date": "2026-10-02", "total": 32.62, "subtotal": 30.50, "tax": 2.12}""";

        var result = await _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token);

        Assert.Equal("Lantern Noodle House", result.Merchant);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Date);
        Assert.Equal(32.62m, result.Total);
        Assert.Equal(30.50m, result.Subtotal);
        Assert.Equal(2.12m, result.Tax);
    }

    [Fact]
    public async Task Reply_with_no_date_gives_an_empty_date_and_no_exception()
    {
        _gateway.Reply = () => """{"merchant": "Cafe", "date": null, "total": 12.50}""";

        var result = await _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token);

        Assert.Null(result.Date);
        Assert.Equal(12.50m, result.Total);
    }

    [Fact]
    public async Task Result_carries_the_configured_model_and_the_prompt_version()
    {
        var result = await _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token);

        Assert.Equal("fake-model:1b", result.Model);
        Assert.Equal(ExtractionPrompt.Version, result.PromptVersion);
    }

    [Fact]
    public async Task Gateway_failure_throws_extraction_exception_with_one_call_and_no_retry()
    {
        _gateway.Failure = new AiGatewayException("The provider returned HTTP 500.");

        var failure = await Assert.ThrowsAsync<ExtractionException>(
            () => _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token));

        Assert.Equal("The provider returned HTTP 500.", failure.Message);
        Assert.IsType<AiGatewayException>(failure.InnerException);
        Assert.Single(_gateway.Requests);
    }

    [Fact]
    public async Task Unparseable_reply_throws_extraction_exception_with_one_call_and_no_retry()
    {
        _gateway.Reply = () => "Sorry, I cannot help with that.";

        await Assert.ThrowsAsync<ExtractionException>(
            () => _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token));

        Assert.Single(_gateway.Requests);
    }

    [Fact]
    public async Task Pdf_that_cannot_be_rendered_throws_extraction_exception_without_calling_the_gateway()
    {
        _renderer.Failure = new PdfRenderException("The PDF could not be read.");

        var failure = await Assert.ThrowsAsync<ExtractionException>(
            () => _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "application/pdf"), Token));

        Assert.Equal("The PDF could not be read.", failure.Message);
        Assert.Empty(_gateway.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_turned_into_an_extraction_failure()
    {
        _gateway.Failure = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "image/png"), Token));
    }

    [Fact]
    public async Task Unsupported_content_type_is_a_programming_error()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _extractor.ExtractAsync(new ReceiptFile(new byte[] { 1 }, "text/plain"), Token));
    }

    [Fact]
    public void Prompt_text_is_pinned_to_its_version()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ExtractionPrompt.Text)));

        Assert.Equal(PinnedPromptVersion, ExtractionPrompt.Version);
        Assert.Equal(PinnedPromptHash, hash);
    }

    [Fact]
    public async Task No_log_line_holds_receipt_content_whether_the_call_succeeds_or_fails()
    {
        await using var harness = GatewayHarness.Create();
        var extractor = new VisionReceiptExtractor(harness.Gateway, _renderer);
        const string merchant = "SECRET-MERCHANT-NAME";
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Reply(
            $$"""{\"merchant\": \"{{merchant}}\", \"date\": \"2026-10-02\", \"total\": 31.41}"""));
        var file = new ReceiptFile(new byte[] { 1, 2, 3 }, "image/png");

        var result = await extractor.ExtractAsync(file, Token);
        harness.Handler.Respond = _ => Task.FromResult(FakeProviderHandler.Status(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<ExtractionException>(() => extractor.ExtractAsync(file, Token));

        var log = string.Join('\n', harness.Logs.Lines);
        Assert.Equal(merchant, result.Merchant);
        Assert.Equal("gemma4:31b", result.Model);
        Assert.Contains("AI call finished", log, StringComparison.Ordinal);
        Assert.DoesNotContain(merchant, log, StringComparison.Ordinal);
        Assert.DoesNotContain("31.41", log, StringComparison.Ordinal);
    }
}
