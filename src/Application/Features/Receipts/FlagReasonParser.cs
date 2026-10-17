using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Application.Features.Receipts;

internal static class FlagReasonParser
{
    /// <summary>FlagReasons is stored as comma-separated names. A name this version does not know is skipped.</summary>
    public static List<FlagReason> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => Enum.TryParse<FlagReason>(name, out var reason) ? (FlagReason?)reason : null)
                .OfType<FlagReason>()
                .ToList();
}
