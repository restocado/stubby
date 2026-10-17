using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stubby.Application.Abstractions;
using Stubby.Application.Features.Receipts.DeleteReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

public class DeleteReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeReceiptRepository _receipts = new();
    private readonly FakeImageStore _images = new();
    private readonly DeleteReceiptCommandHandler _handler;

    public DeleteReceiptTests()
    {
        _handler = new DeleteReceiptCommandHandler(_receipts, _images, NullLogger<DeleteReceiptCommandHandler>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ReceiptStatus.ForReview)]
    [InlineData(ReceiptStatus.Failed)]
    [InlineData(ReceiptStatus.Approved)]
    public async Task A_receipt_that_is_ForReview_Failed_or_Approved_is_deleted_with_its_file(ReceiptStatus status)
    {
        var receipt = await AddAsync(status);
        var path = receipt.ImagePath;

        var response = await _handler.Handle(new DeleteReceiptCommand(receipt.Id), Token);

        Assert.Equal(receipt.TransactionNo, Assert.IsType<DeleteReceiptResponse.Deleted>(response).TransactionNo);
        Assert.Empty(_receipts.Receipts);
        Assert.False(_images.Files.ContainsKey(path));
    }

    [Theory]
    [InlineData(ReceiptStatus.Queued)]
    [InlineData(ReceiptStatus.Processing)]
    public async Task A_Queued_or_Processing_receipt_returns_conflict_and_removes_nothing(ReceiptStatus status)
    {
        var receipt = await AddAsync(status);

        var response = await _handler.Handle(new DeleteReceiptCommand(receipt.Id), Token);

        Assert.IsType<DeleteReceiptResponse.Conflict>(response);
        Assert.Single(_receipts.Receipts);
        Assert.True(_images.Files.ContainsKey(receipt.ImagePath));
    }

    [Fact]
    public async Task An_unknown_id_returns_not_found()
    {
        await AddAsync(ReceiptStatus.ForReview);

        var response = await _handler.Handle(new DeleteReceiptCommand(Guid.NewGuid()), Token);

        Assert.IsType<DeleteReceiptResponse.NotFound>(response);
        Assert.Single(_receipts.Receipts);
        Assert.Single(_images.Files);
    }

    [Fact]
    public async Task Field_records_stay_when_an_Approved_receipt_is_deleted()
    {
        var receipt = await AddAsync(ReceiptStatus.Approved);
        var before = _receipts.FieldRecords.ToList();
        Assert.Equal(3, before.Count);

        await _handler.Handle(new DeleteReceiptCommand(receipt.Id), Token);

        Assert.Equal(before, _receipts.FieldRecords);
        Assert.Empty(_receipts.Receipts);
    }

    [Fact]
    public void A_field_record_holds_nothing_that_identifies_its_receipt()
    {
        var names = typeof(FieldRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Receipt", StringComparison.Ordinal));
        Assert.DoesNotContain("TransactionNo", names);
        Assert.DoesNotContain("ImagePath", names);
    }

    [Fact]
    public async Task A_failed_file_delete_still_returns_success()
    {
        var receipt = await AddAsync(ReceiptStatus.Failed);
        var handler = new DeleteReceiptCommandHandler(_receipts, new ThrowingDeleteStore(), NullLogger<DeleteReceiptCommandHandler>.Instance);

        var response = await handler.Handle(new DeleteReceiptCommand(receipt.Id), Token);

        Assert.IsType<DeleteReceiptResponse.Deleted>(response);
        Assert.Empty(_receipts.Receipts);
    }

    [Fact]
    public async Task A_failed_file_delete_is_logged_without_receipt_content()
    {
        var receipt = await AddAsync(ReceiptStatus.Failed);
        var log = new CapturingLogger();
        var handler = new DeleteReceiptCommandHandler(_receipts, new ThrowingDeleteStore(), log);

        await handler.Handle(new DeleteReceiptCommand(receipt.Id), Token);

        var line = Assert.Single(log.Lines);
        Assert.Contains("could not be removed", line, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.OriginalFileName, line, StringComparison.Ordinal);
        Assert.DoesNotContain(receipt.ImagePath, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_database_delete_leaves_the_file()
    {
        var receipt = await AddAsync(ReceiptStatus.Failed);
        _receipts.ExceptionOnDelete = new InvalidOperationException("The database is down.");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _handler.Handle(new DeleteReceiptCommand(receipt.Id), Token));

        Assert.Single(_receipts.Receipts);
        Assert.True(_images.Files.ContainsKey(receipt.ImagePath));
    }

    private async Task<Receipt> AddAsync(ReceiptStatus status)
    {
        var n = _receipts.Receipts.Count;
        var path = await _images.SaveAsync(new byte[] { 1, 2, 3, (byte)n }, "image/png", Token);
        var receipt = Receipt.Create($"r{n}.png", "image/png", 4, $"checksum-{n}", path, Now.AddMinutes(-n));
        Assert.True(await _receipts.TryAddAsync(receipt, Token));
        if (status != ReceiptStatus.Queued)
        {
            receipt.StartProcessing(Now);
        }

        switch (status)
        {
            case ReceiptStatus.Failed:
                receipt.FailAttempt("failed", 1, Now);
                break;
            case ReceiptStatus.ForReview or ReceiptStatus.Approved:
                receipt.CompleteExtraction("Cafe", new DateOnly(2026, 10, 2), 5m, "m", "v1", false, null, Now);
                if (status == ReceiptStatus.Approved)
                {
                    _receipts.FieldRecords.AddRange(receipt.Approve("Cafe", new DateOnly(2026, 10, 2), 5m, ReceiptCategory.Dining, Now));
                }

                break;
        }

        return receipt;
    }

    private sealed class ThrowingDeleteStore : IImageStore
    {
        public Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("The disk is read-only."));
    }

    private sealed class CapturingLogger : ILogger<DeleteReceiptCommandHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
