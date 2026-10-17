using Mediator;
using Stubby.Application.Features.Receipts.GetReceiptImage;

namespace Stubby.Web.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        // The image needs a URL, because an <img> or an <iframe> cannot call a handler. It takes an id and never a path.
        app.MapGet("/api/receipts/{id:guid}/image", async (Guid id, IMediator mediator, HttpContext http, CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new GetReceiptImageQuery(id), cancellationToken);
            if (response is not GetReceiptImageResponse.Found found)
            {
                return response is GetReceiptImageResponse.Locked ? Results.Conflict() : Results.NotFound();
            }

            // Shown inline, with its stored type, and never sniffed into a different one. Receipts are private.
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.File(found.Content, found.ContentType);
        });

        return app;
    }
}
