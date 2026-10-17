using Mediator;

namespace Stubby.Application.Features.Receipts.DeleteReceipt;

/// <summary>
/// Removes a ForReview, Failed or Approved receipt and its file for good. Its field records stay,
/// with nothing that points back at the receipt (NFR-D2).
/// </summary>
public sealed record DeleteReceiptCommand(Guid ReceiptId) : ICommand<DeleteReceiptResponse>;
