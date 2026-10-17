using Mediator;

namespace Stubby.Application.Features.Receipts.ReprocessAllFailedReceipts;

/// <summary>Puts every Failed receipt back in the queue, for example after the provider was down.</summary>
public sealed record ReprocessAllFailedReceiptsCommand : ICommand<ReprocessAllFailedReceiptsResponse>;
