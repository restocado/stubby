namespace Stubby.Application.Abstractions;

/// <summary>
/// How deleting a receipt ended. When it is <see cref="ReceiptWriteOutcome.Done"/> the row is gone, and
/// <paramref name="ImagePath"/> is where its file was, so the caller can remove the file after the commit.
/// </summary>
public sealed record ReceiptDeletion(ReceiptWriteOutcome Outcome, long TransactionNo = 0, string? ImagePath = null);
