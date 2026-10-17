using Stubby.Application.Features.Receipts.UploadReceipt;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Tests.Receipts;

public class UploadReceiptTests
{
    private const string Jpeg = "image/jpeg";
    private const string Png = "image/png";
    private const string Pdf = "application/pdf";

    private readonly FakeReceiptRepository _receipts = new();
    private readonly FakeImageStore _images = new();
    private readonly UploadReceiptCommandHandler _handler;

    public UploadReceiptTests()
    {
        _handler = new UploadReceiptCommandHandler(
            new UploadReceiptCommandValidator(),
            _receipts,
            _images,
            TimeProvider.System);
    }

    [Theory]
    [InlineData("receipt.jpg", Jpeg)]
    [InlineData("receipt.JPEG", Jpeg)]
    [InlineData("receipt.png", Png)]
    [InlineData("receipt.pdf", Pdf)]
    public async Task Valid_file_is_queued(string fileName, string contentType)
    {
        var response = await Upload(fileName, contentType, Content(contentType, 200));

        var created = Assert.IsType<UploadReceiptResponse.Created>(response);
        var receipt = Assert.Single(_receipts.Receipts);
        Assert.Equal(created.Id, receipt.Id);
        Assert.Equal(created.TransactionNo, receipt.TransactionNo);
        Assert.Equal(ReceiptStatus.Queued, receipt.Status);
        Assert.Equal(contentType, receipt.ContentType);
        Assert.Equal(200, receipt.FileSizeBytes);
        Assert.Single(_images.Files);
    }

    [Theory]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("picture.gif", "image/gif")]
    [InlineData("receipt.jpg", "text/plain")]
    [InlineData("receipt.png", Jpeg)]
    [InlineData("receipt", Jpeg)]
    public async Task Unsupported_type_or_mismatched_extension_is_rejected(string fileName, string contentType)
    {
        var response = await Upload(fileName, contentType, Content(Jpeg, 200));

        Assert.IsType<UploadReceiptResponse.UnsupportedType>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task File_whose_bytes_do_not_match_its_type_is_rejected()
    {
        var response = await Upload("receipt.png", Png, Content(Pdf, 200));

        Assert.IsType<UploadReceiptResponse.UnsupportedType>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task File_of_the_maximum_size_is_accepted()
    {
        var response = await Upload("receipt.pdf", Pdf, Content(Pdf, Receipt.MaxFileSizeBytes));

        Assert.IsType<UploadReceiptResponse.Created>(response);
        Assert.Equal(Receipt.MaxFileSizeBytes, Assert.Single(_receipts.Receipts).FileSizeBytes);
    }

    [Fact]
    public async Task File_one_byte_over_the_maximum_is_rejected()
    {
        var response = await Upload("receipt.pdf", Pdf, Content(Pdf, Receipt.MaxFileSizeBytes + 1));

        Assert.IsType<UploadReceiptResponse.TooLarge>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Declared_size_is_not_trusted()
    {
        var content = Content(Pdf, Receipt.MaxFileSizeBytes + 1);

        var response = await _handler.Handle(
            new UploadReceiptCommand("receipt.pdf", Pdf, 100, new MemoryStream(content)),
            CancellationToken.None);

        Assert.IsType<UploadReceiptResponse.TooLarge>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        var response = await Upload("receipt.pdf", Pdf, []);

        Assert.IsType<UploadReceiptResponse.Empty>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Empty_stream_with_a_declared_size_is_rejected()
    {
        var response = await _handler.Handle(
            new UploadReceiptCommand("receipt.pdf", Pdf, 100, new MemoryStream()),
            CancellationToken.None);

        Assert.IsType<UploadReceiptResponse.Empty>(response);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Same_bytes_twice_return_the_existing_transaction_number()
    {
        var content = Content(Png, 300);
        var first = Assert.IsType<UploadReceiptResponse.Created>(await Upload("a.png", Png, content));

        var second = await Upload("b.png", Png, content);

        var duplicate = Assert.IsType<UploadReceiptResponse.Duplicate>(second);
        Assert.Equal(first.TransactionNo, duplicate.ExistingTransactionNo);
        Assert.Single(_receipts.Receipts);
        Assert.Single(_images.Files);
    }

    [Fact]
    public async Task Different_files_get_increasing_transaction_numbers()
    {
        var first = Assert.IsType<UploadReceiptResponse.Created>(await Upload("a.png", Png, Content(Png, 300, 1)));
        var second = Assert.IsType<UploadReceiptResponse.Created>(await Upload("b.png", Png, Content(Png, 300, 2)));

        Assert.True(second.TransactionNo > first.TransactionNo);
    }

    [Fact]
    public async Task Duplicate_does_not_use_up_a_transaction_number()
    {
        var content = Content(Png, 300);
        await Upload("a.png", Png, content);
        await Upload("a-again.png", Png, content);

        var next = Assert.IsType<UploadReceiptResponse.Created>(await Upload("b.png", Png, Content(Png, 300, 2)));

        Assert.Equal(2, next.TransactionNo);
    }

    [Fact]
    public async Task Losing_a_race_to_an_identical_upload_returns_the_winners_number_and_removes_the_file()
    {
        var content = Content(Png, 300);
        var winner = Receipt.Create("winner.png", Png, content.Length, Checksum(content), "winner-path", DateTimeOffset.UtcNow);
        _receipts.ReceiptToAddBeforeNextInsert = winner;

        var response = await Upload("loser.png", Png, content);

        var duplicate = Assert.IsType<UploadReceiptResponse.Duplicate>(response);
        Assert.Equal(winner.TransactionNo, duplicate.ExistingTransactionNo);
        Assert.Single(_receipts.Receipts);
        Assert.Empty(_images.Files);
    }

    [Fact]
    public async Task Save_failure_removes_the_stored_file()
    {
        _receipts.ExceptionOnAdd = new InvalidOperationException("database is down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Upload("receipt.png", Png, Content(Png, 300)));

        Assert.Empty(_receipts.Receipts);
        Assert.Empty(_images.Files);
    }

    private ValueTask<UploadReceiptResponse> Upload(string fileName, string contentType, byte[] content) =>
        _handler.Handle(
            new UploadReceiptCommand(fileName, contentType, content.Length, new MemoryStream(content)),
            CancellationToken.None);

    private void AssertNothingSaved()
    {
        Assert.Empty(_receipts.Receipts);
        Assert.Empty(_images.Files);
    }

    private static string Checksum(byte[] content) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

    /// <summary>Bytes that start with the signature of <paramref name="contentType"/>, padded to <paramref name="size"/>.</summary>
    private static byte[] Content(string contentType, int size, byte fill = 0)
    {
        byte[] signature = contentType switch
        {
            Jpeg => [0xFF, 0xD8, 0xFF],
            Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            _ => "%PDF-"u8.ToArray(),
        };

        var content = new byte[size];
        Array.Fill(content, fill);
        signature.CopyTo(content, 0);
        return content;
    }
}
