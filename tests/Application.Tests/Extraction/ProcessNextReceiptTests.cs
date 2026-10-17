using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Extraction.ProcessNextReceipt;
using Stubby.Application.Tests.Receipts;
using Stubby.Domain.Receipts;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Tests.Extraction;

public class ProcessNextReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly FakeImageStore _images = new();
    private readonly FakeReceiptExtractor _extractor = new();
    private readonly FakeExtractionSettings _settings = new();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly CapturingLogger _log = new();
    private readonly ProcessNextReceiptCommandHandler _handler;

    public ProcessNextReceiptTests()
    {
        _handler = new ProcessNextReceiptCommandHandler(_receipts, _images, _extractor, _settings, _clock, _log);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Success_ends_ForReview_with_the_extracted_fields_and_no_attempt_charged()
    {
        _extractor.Result = new ExtractionResult(
            "Lantern Noodle House", new DateOnly(2026, 10, 2), 32.62m, 26.50m, 2.12m, "gemma4:31b", "v1");
        var receipt = await AddQueued(minutesOld: 0);

        var result = await Process();

        var completed = Assert.IsType<ProcessNextReceiptResult.Completed>(result);
        Assert.Equal(receipt.Id, completed.ReceiptId);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.Equal("Lantern Noodle House", receipt.ExtractedMerchant);
        Assert.Equal(new DateOnly(2026, 10, 2), receipt.ExtractedDate);
        Assert.Equal(32.62m, receipt.ExtractedTotal);
        Assert.Equal("gemma4:31b", receipt.Model);
        Assert.Equal("v1", receipt.PromptVersion);
        Assert.Equal(0, receipt.AttemptsUsed);
        Assert.Equal(1, _receipts.Saves);
    }

    [Fact]
    public async Task The_extractor_gets_the_stored_file_and_its_content_type()
    {
        var receipt = await AddQueued(minutesOld: 0, contentType: "application/pdf", content: [7, 8, 9]);

        await Process();

        var file = Assert.Single(_extractor.Files);
        Assert.Equal([7, 8, 9], file.Content.ToArray());
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(receipt.ContentType, file.ContentType);
    }

    [Fact]
    public async Task Nothing_queued_changes_nothing()
    {
        var done = await AddQueued(minutesOld: 0);
        done.StartProcessing(Now);
        done.FailAttempt("old failure", 1, Now);

        var result = await Process();

        Assert.IsType<ProcessNextReceiptResult.NothingToDo>(result);
        Assert.Equal(ReceiptStatus.Failed, done.Status);
        Assert.Equal(0, _receipts.Saves);
        Assert.Empty(_extractor.Files);
    }

    [Fact]
    public async Task The_oldest_queued_receipt_is_processed_first()
    {
        var newest = await AddQueued(minutesOld: 1);
        var oldest = await AddQueued(minutesOld: 30);
        var middle = await AddQueued(minutesOld: 10);

        var order = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            order.Add(Assert.IsType<ProcessNextReceiptResult.Completed>(await Process()).ReceiptId);
        }

        Assert.Equal([oldest.Id, middle.Id, newest.Id], order);
        Assert.IsType<ProcessNextReceiptResult.NothingToDo>(await Process());
    }

    [Fact]
    public async Task Total_at_or_above_the_limit_ends_ForReview_flagged()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Total = 3500.00m };
        var receipt = await AddQueued(minutesOld: 0);

        var result = await Process();

        Assert.True(Assert.IsType<ProcessNextReceiptResult.Completed>(result).IsFlagged);
        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.True(receipt.IsFlagged);
        Assert.Equal(nameof(FlagReason.TotalAtOrAboveLimit), receipt.FlagReasons);
    }

    [Fact]
    public async Task Missing_total_ends_ForReview_flagged_and_not_Failed()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Total = null };
        var receipt = await AddQueued(minutesOld: 0);

        await Process();

        Assert.Equal(ReceiptStatus.ForReview, receipt.Status);
        Assert.True(receipt.IsFlagged);
        Assert.Equal(nameof(FlagReason.TotalMissing), receipt.FlagReasons);
        Assert.Equal(0, receipt.AttemptsUsed);
    }

    [Fact]
    public async Task The_flag_limit_comes_from_the_settings_on_every_call()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Total = 4000.00m };
        var first = await AddQueued(minutesOld: 20);
        var second = await AddQueued(minutesOld: 10);

        await Process();
        _settings.FlagTotalLimit = 5000m;
        await Process();

        Assert.True(first.IsFlagged);
        Assert.False(second.IsFlagged);
    }

    [Fact]
    public async Task A_subtotal_and_tax_mismatch_flags_the_receipt_and_neither_is_kept()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Total = 10.00m, Subtotal = 10.00m, Tax = 0.80m };
        var receipt = await AddQueued(minutesOld: 0);

        await Process();

        Assert.Equal(nameof(FlagReason.SubtotalTaxMismatch), receipt.FlagReasons);
        Assert.DoesNotContain(
            typeof(Receipt).GetProperties(),
            p => p.Name.Contains("Subtotal", StringComparison.Ordinal) || p.Name.Equals("Tax", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Today_for_the_date_flags_is_the_local_date()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Date = new DateOnly(2026, 10, 18) };
        var receipt = await AddQueued(minutesOld: 0);

        await Process();

        Assert.Equal(nameof(FlagReason.DateInFuture), receipt.FlagReasons);
    }

    [Fact]
    public async Task With_max_1_a_failing_extractor_ends_Failed_after_one_call()
    {
        _extractor.Failure = new ExtractionException("The provider returned HTTP 500.");
        var receipt = await AddQueued(minutesOld: 0);

        var result = await Process();

        var failed = Assert.IsType<ProcessNextReceiptResult.Failed>(result);
        Assert.False(failed.WillRetry);
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal(1, receipt.AttemptsUsed);
        Assert.Equal("The provider returned HTTP 500.", receipt.LastError);
        Assert.Single(_extractor.Files);
    }

    [Fact]
    public async Task With_max_3_the_receipt_is_Queued_Queued_then_Failed_across_three_calls()
    {
        _settings.MaxAttempts = 3;
        _extractor.Failure = new ExtractionException("The call timed out.");
        var receipt = await AddQueued(minutesOld: 0);

        var statuses = new List<ReceiptStatus>();
        for (var i = 0; i < 3; i++)
        {
            await Process();
            statuses.Add(receipt.Status);
        }

        Assert.Equal([ReceiptStatus.Queued, ReceiptStatus.Queued, ReceiptStatus.Failed], statuses);
        Assert.Equal(3, receipt.AttemptsUsed);
        Assert.Equal(3, _extractor.Files.Count);
    }

    [Fact]
    public async Task Failed_attempt_reports_whether_it_will_retry()
    {
        _settings.MaxAttempts = 2;
        _extractor.Failure = new ExtractionException("The call timed out.");
        await AddQueued(minutesOld: 0);

        var first = Assert.IsType<ProcessNextReceiptResult.Failed>(await Process());
        var second = Assert.IsType<ProcessNextReceiptResult.Failed>(await Process());

        Assert.True(first.WillRetry);
        Assert.False(second.WillRetry);
    }

    [Fact]
    public async Task Max_attempts_is_read_for_every_call()
    {
        _extractor.Failure = new ExtractionException("The call timed out.");
        var receipt = await AddQueued(minutesOld: 0);

        await Process();
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);

        receipt.Reprocess(Now);
        _settings.MaxAttempts = 2;
        await Process();

        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
    }

    [Fact]
    public async Task A_missing_file_counts_as_a_failed_attempt_and_the_extractor_is_not_called()
    {
        var receipt = await AddQueued(minutesOld: 0);
        _images.Files.Clear();

        var result = await Process();

        Assert.IsType<ProcessNextReceiptResult.Failed>(result);
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal(1, receipt.AttemptsUsed);
        Assert.Equal("The stored file is missing.", receipt.LastError);
        Assert.Empty(_extractor.Files);
    }

    [Fact]
    public async Task An_unreadable_file_counts_as_a_failed_attempt()
    {
        var receipt = await AddQueued(minutesOld: 0);
        var images = new ThrowingImageStore(new UnauthorizedAccessException("denied"));
        var handler = new ProcessNextReceiptCommandHandler(_receipts, images, _extractor, _settings, _clock, _log);

        await handler.Handle(new ProcessNextReceiptCommand(), Token);

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("The stored file could not be read.", receipt.LastError);
    }

    [Fact]
    public async Task A_gateway_timeout_counts_as_a_failed_attempt()
    {
        _extractor.Failure = new ExtractionException("The call timed out.", new AiGatewayException("The call timed out."));
        var receipt = await AddQueued(minutesOld: 0);

        await Process();

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("The call timed out.", receipt.LastError);
    }

    [Fact]
    public async Task An_unexpected_error_is_a_failed_attempt_and_never_reaches_the_worker_or_the_stored_error()
    {
        _extractor.Failure = new InvalidOperationException("SECRET-RECEIPT-CONTENT");
        var receipt = await AddQueued(minutesOld: 0);

        var result = await Process();

        Assert.IsType<ProcessNextReceiptResult.Failed>(result);
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("An unexpected error occurred.", receipt.LastError);
        Assert.DoesNotContain("SECRET", receipt.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_bad_receipt_does_not_stop_the_next_one()
    {
        var bad = await AddQueued(minutesOld: 20);
        var good = await AddQueued(minutesOld: 10);
        _extractor.OnExtract = _ =>
        {
            _extractor.Failure = _extractor.Files.Count == 1 ? new InvalidOperationException("boom") : null;
        };
        _extractor.Failure = new InvalidOperationException("boom");

        await Process();
        await Process();

        Assert.Equal(ReceiptStatus.Failed, bad.Status);
        Assert.Equal(ReceiptStatus.ForReview, good.Status);
    }

    [Fact]
    public async Task Cancelling_mid_extraction_requeues_without_charging_an_attempt()
    {
        var receipt = await AddQueued(minutesOld: 0);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _extractor.OnExtract = _ => shutdown.Cancel();
        _extractor.Failure = new OperationCanceledException(shutdown.Token);

        var result = await _handler.Handle(new ProcessNextReceiptCommand(), shutdown.Token);

        var interrupted = Assert.IsType<ProcessNextReceiptResult.Interrupted>(result);
        Assert.Equal(receipt.Id, interrupted.ReceiptId);
        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
        Assert.Equal(0, receipt.AttemptsUsed);
        Assert.Null(receipt.LastError);
        Assert.Equal(1, _receipts.Saves);
    }

    [Fact]
    public async Task A_cancellation_that_was_not_a_shutdown_is_a_failed_attempt()
    {
        _extractor.Failure = new OperationCanceledException();
        var receipt = await AddQueued(minutesOld: 0);

        var result = await Process();

        Assert.IsType<ProcessNextReceiptResult.Failed>(result);
        Assert.Equal(1, receipt.AttemptsUsed);
    }

    [Fact]
    public async Task Invalid_settings_throw_before_any_receipt_is_claimed()
    {
        _settings.Failure = new InvalidOperationException("Setting 'Extraction:MaxAttempts' must be at least 1.");
        var receipt = await AddQueued(minutesOld: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Process());

        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
        Assert.Empty(_extractor.Files);
    }

    [Fact]
    public async Task No_log_line_holds_receipt_content()
    {
        _extractor.Result = FakeReceiptExtractor.SampleResult with { Merchant = "SECRET-MERCHANT-NAME", Total = 31.41m };
        await AddQueued(minutesOld: 20);
        await Process();
        _extractor.Failure = new InvalidOperationException("SECRET-FAILURE-TEXT");
        await AddQueued(minutesOld: 10, content: [5, 5, 5]);
        await Process();

        var lines = string.Join('\n', _log.Lines.Where(l => !l.Contains("SECRET-FAILURE-TEXT", StringComparison.Ordinal)));

        Assert.Contains("ready for review", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-MERCHANT-NAME", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("31.41", lines, StringComparison.Ordinal);
    }

    private async Task<ProcessNextReceiptResult> Process() =>
        await _handler.Handle(new ProcessNextReceiptCommand(), Token);

    private async Task<Receipt> AddQueued(
        int minutesOld, string contentType = "image/png", byte[]? content = null)
    {
        var bytes = content ?? [1, 2, 3, (byte)_receipts.Receipts.Count];
        var path = await _images.SaveAsync(bytes, contentType, Token);
        var receipt = Receipt.Create(
            $"receipt-{_receipts.Receipts.Count}.png", contentType, bytes.Length, $"checksum-{_receipts.Receipts.Count}", path,
            Now.AddMinutes(-minutesOld));
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        return receipt;
    }

    private sealed class CapturingLogger : ILogger<ProcessNextReceiptCommandHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"[{logLevel}] {formatter(state, exception)} {exception?.Message}");
    }

    private sealed class ThrowingImageStore(Exception failure) : IImageStore
    {
        public Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromException<byte[]>(failure);

        public Task DeleteAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
