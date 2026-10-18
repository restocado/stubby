using Mediator;

namespace Stubby.Application.Features.Receipts.GetReviewQueue;

/// <summary>Where a receipt stands among the receipts waiting for review, and which one follows it.</summary>
public sealed record GetReviewQueueQuery(Guid CurrentId) : IQuery<GetReviewQueueResponse>;
