using System.Security.Cryptography;
using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.UploadReceipt;

public sealed class UploadReceiptCommandHandler(
    IValidator<UploadReceiptCommand> validator,
    IReceiptRepository receipts,
    IImageStore imageStore,
    TimeProvider timeProvider) : ICommandHandler<UploadReceiptCommand, UploadReceiptResponse>
{
    public async ValueTask<UploadReceiptResponse> Handle(UploadReceiptCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Reject(validation.Errors[0].ErrorCode);
        }

        // The declared size is only a hint, so read at most one byte past the limit.
        var content = await ReadAtMostAsync(command.Content, Receipt.MaxFileSizeBytes + 1, cancellationToken);
        if (content.Length == 0)
        {
            return new UploadReceiptResponse.Empty();
        }

        if (content.Length > Receipt.MaxFileSizeBytes)
        {
            return new UploadReceiptResponse.TooLarge();
        }

        if (!UploadFileTypes.SignatureMatches(command.ContentType, content))
        {
            return new UploadReceiptResponse.UnsupportedType();
        }

        var checksum = Convert.ToHexStringLower(SHA256.HashData(content));
        if (await receipts.FindTransactionNoByChecksumAsync(checksum, cancellationToken) is { } existing)
        {
            return new UploadReceiptResponse.Duplicate(existing);
        }

        var path = await imageStore.SaveAsync(content, command.ContentType, cancellationToken);
        try
        {
            var receipt = Receipt.Create(
                Path.GetFileName(command.FileName),
                command.ContentType,
                content.Length,
                checksum,
                path,
                timeProvider.GetUtcNow());

            if (await receipts.TryAddAsync(receipt, cancellationToken))
            {
                return new UploadReceiptResponse.Created(receipt.Id, receipt.TransactionNo);
            }
        }
        catch
        {
            await imageStore.DeleteAsync(path, CancellationToken.None);
            throw;
        }

        // Lost a race with an identical upload: drop our copy and report the winner's number.
        await imageStore.DeleteAsync(path, CancellationToken.None);
        var winner = await receipts.FindTransactionNoByChecksumAsync(checksum, cancellationToken)
            ?? throw new InvalidOperationException("A duplicate checksum was reported but no receipt has it.");
        return new UploadReceiptResponse.Duplicate(winner);
    }

    private static UploadReceiptResponse Reject(string errorCode) => errorCode switch
    {
        UploadReceiptCommandValidator.EmptyCode => new UploadReceiptResponse.Empty(),
        UploadReceiptCommandValidator.TooLargeCode => new UploadReceiptResponse.TooLarge(),
        _ => new UploadReceiptResponse.UnsupportedType(),
    };

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        while (buffer.Length < limit)
        {
            var wanted = (int)Math.Min(chunk.Length, limit - buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
