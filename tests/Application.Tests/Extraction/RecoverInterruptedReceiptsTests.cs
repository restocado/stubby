using Microsoft.Extensions.Logging;
using Stubby.Application.Features.Extraction.RecoverInterruptedReceipts;
using Stubby.Application.Tests.Receipts;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Extraction;

public class RecoverInterruptedReceiptsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly CapturingLogger _log = new();
    private readonly RecoverInterruptedReceiptsCommandHandler _handler;

    public RecoverInterruptedReceiptsTests()
    {
        _handler = new RecoverInterruptedReceiptsCommandHandler(_receipts, new FixedTimeProvider(Now), _log);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_Processing_receipt_goes_back_to_Queued_with_its_attempts_unchanged()
    {
        var firstTry = await AddAsync(1);
        firstTry.StartProcessing(Now);
        var retry = await AddAsync(2);
        retry.StartProcessing(Now);
        retry.FailAttempt("failed once", maxAttempts: 3, Now);
        retry.StartProcessing(Now);

        var count = await _handler.Handle(new RecoverInterruptedReceiptsCommand(), Token);

        Assert.Equal(2, count);
        Assert.Equal([ReceiptStatus.Queued, ReceiptStatus.Queued], [firstTry.Status, retry.Status]);
        Assert.Equal(0, firstTry.AttemptsUsed);
        Assert.Equal(1, retry.AttemptsUsed);
        Assert.Equal("failed once", retry.LastError);
    }

    [Fact]
    public async Task Receipts_in_other_statuses_are_left_alone()
    {
        var queued = await AddAsync(1);
        var failed = await AddAsync(2);
        failed.StartProcessing(Now);
        failed.FailAttempt("failed", maxAttempts: 1, Now);
        var forReview = await AddAsync(3);
        forReview.StartProcessing(Now);
        forReview.CompleteExtraction("Cafe", new DateOnly(2026, 10, 2), 5m, "m", "v1", isFlagged: false, null, Now);

        var count = await _handler.Handle(new RecoverInterruptedReceiptsCommand(), Token);

        Assert.Equal(0, count);
        Assert.Equal(
            [ReceiptStatus.Queued, ReceiptStatus.Failed, ReceiptStatus.ForReview],
            [queued.Status, failed.Status, forReview.Status]);
    }

    [Fact]
    public async Task With_nothing_interrupted_the_count_is_zero()
    {
        Assert.Equal(0, await _handler.Handle(new RecoverInterruptedReceiptsCommand(), Token));
    }

    [Fact]
    public async Task The_count_is_logged()
    {
        (await AddAsync(1)).StartProcessing(Now);

        await _handler.Handle(new RecoverInterruptedReceiptsCommand(), Token);

        Assert.Contains(_log.Lines, line => line.Contains("Returned 1 interrupted receipts", StringComparison.Ordinal));
    }

    private async Task<Receipt> AddAsync(int n)
    {
        var receipt = Receipt.Create($"r{n}.png", "image/png", 10, $"checksum-{n}", $"path-{n}", Now.AddMinutes(-n));
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        return receipt;
    }

    private sealed class CapturingLogger : ILogger<RecoverInterruptedReceiptsCommandHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
