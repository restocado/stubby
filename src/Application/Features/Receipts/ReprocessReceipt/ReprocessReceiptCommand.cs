using Mediator;

namespace Stubby.Application.Features.Receipts.ReprocessReceipt;

/// <summary>Puts one Failed receipt back in the queue with a fresh set of attempts. The same file is read again.</summary>
public sealed record ReprocessReceiptCommand(Guid ReceiptId) : ICommand<ReprocessReceiptResponse>;
