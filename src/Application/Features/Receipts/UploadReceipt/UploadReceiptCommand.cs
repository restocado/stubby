using Mediator;

namespace Stubby.Application.Features.Receipts.UploadReceipt;

public sealed record UploadReceiptCommand(string FileName, string ContentType, long Size, Stream Content)
    : ICommand<UploadReceiptResponse>;
