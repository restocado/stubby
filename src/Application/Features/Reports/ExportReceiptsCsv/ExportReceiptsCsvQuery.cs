using Mediator;

namespace Stubby.Application.Features.Reports.ExportReceiptsCsv;

/// <summary>Every Approved receipt as a CSV, or only one month's when both <paramref name="Year"/> and <paramref name="Month"/> are given.</summary>
public sealed record ExportReceiptsCsvQuery(int? Year = null, int? Month = null) : IQuery<ExportReceiptsCsvResponse>;
