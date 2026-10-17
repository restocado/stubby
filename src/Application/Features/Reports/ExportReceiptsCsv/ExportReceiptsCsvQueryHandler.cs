using System.Globalization;
using FluentValidation;
using Mediator;
using Stubby.Application.Abstractions;

namespace Stubby.Application.Features.Reports.ExportReceiptsCsv;

public sealed class ExportReceiptsCsvQueryHandler(
    IValidator<ExportReceiptsCsvQuery> validator,
    IReportReader reports,
    TimeProvider timeProvider) : IQueryHandler<ExportReceiptsCsvQuery, ExportReceiptsCsvResponse>
{
    public async ValueTask<ExportReceiptsCsvResponse> Handle(ExportReceiptsCsvQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var rows = await reports.ListApprovedAsync(query.Year, query.Month, cancellationToken);

        // The whole export is dated by the day it was made. A month's export is named for the month.
        var fileName = query.Year is { } year && query.Month is { } month
            ? string.Create(CultureInfo.InvariantCulture, $"stubby-receipts-{year:D4}-{month:D2}.csv")
            : string.Create(CultureInfo.InvariantCulture, $"stubby-receipts-{timeProvider.GetLocalNow():yyyyMMdd}.csv");

        return new ExportReceiptsCsvResponse(ReceiptsCsv.Build(rows), fileName);
    }
}
