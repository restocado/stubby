using Mediator;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.ApproveReceipt;

/// <summary>
/// Saves the final values and category of a ForReview or Failed receipt and locks it. The values may be null here,
/// because they come from a form: the validator turns a missing one into an error for that field.
/// </summary>
public sealed record ApproveReceiptCommand(
    Guid ReceiptId,
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    ReceiptCategory? Category) : ICommand<ApproveReceiptResponse>;
