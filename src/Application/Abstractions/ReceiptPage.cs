using Stubby.Domain.Receipts;

namespace Stubby.Application.Abstractions;

public sealed record ReceiptPage(IReadOnlyList<Receipt> Items, int TotalCount);
