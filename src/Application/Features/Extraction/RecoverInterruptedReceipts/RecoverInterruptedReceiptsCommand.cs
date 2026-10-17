using Mediator;

namespace Stubby.Application.Features.Extraction.RecoverInterruptedReceipts;

/// <summary>
/// Returns every Processing receipt to Queued, with its attempts unchanged, because no attempt finished (NFR-R2).
/// Run once at startup, before the worker's loop. Returns how many receipts were requeued.
/// </summary>
public sealed record RecoverInterruptedReceiptsCommand : ICommand<int>;
