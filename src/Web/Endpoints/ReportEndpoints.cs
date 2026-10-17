using FluentValidation;
using Mediator;
using Stubby.Application.Features.Reports.ExportReceiptsCsv;

namespace Stubby.Web.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // A download needs a URL, because a link cannot call a handler. Without parameters it exports every
        // Approved receipt. With both year and month it exports that month.
        app.MapGet("/api/reports/receipts.csv", async (int? year, int? month, IMediator mediator, HttpContext http, CancellationToken cancellationToken) =>
        {
            try
            {
                var export = await mediator.Send(new ExportReceiptsCsvQuery(year, month), cancellationToken);

                // Receipts are private, and a browser must not guess a different type for the file.
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.CacheControl = "private, no-store";
                return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
            }
            catch (ValidationException ex)
            {
                return Results.BadRequest(ex.Errors.Select(e => e.ErrorMessage).FirstOrDefault() ?? "The request is not valid.");
            }
        });

        return app;
    }
}
