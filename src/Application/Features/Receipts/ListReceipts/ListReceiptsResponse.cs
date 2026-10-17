using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Receipts.ListReceipts;

public sealed record ListReceiptsResponse(IReadOnlyList<ReceiptListItem> Items, int TotalCount, int PageCount);

public sealed record ReceiptListItem(
    Guid Id,
    long TransactionNo,
    string OriginalFileName,
    ReceiptStatus Status,
    bool IsFlagged,
    DateTimeOffset CreatedAt,
    string? Merchant,
    DateOnly? ReceiptDate,
    decimal? Total,
    ReceiptCategory? Category,
    bool CanOpen,
    bool CanDelete);
