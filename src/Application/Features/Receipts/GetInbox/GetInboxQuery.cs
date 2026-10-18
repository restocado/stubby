using Mediator;

namespace Stubby.Application.Features.Receipts.GetInbox;

/// <summary>What needs attention now: receipts to review, receipts that could not be read, and how many are being read.</summary>
public sealed record GetInboxQuery : IQuery<GetInboxResponse>
{
    /// <summary>The most receipts of each kind that are listed. The counts are always the full counts.</summary>
    public const int Limit = 50;
}
