using Mediator;

namespace Stubby.Application.Features.Receipts.GetReceiptImage;

/// <summary>Reads a receipt's stored file. The caller gives an id and never a path.</summary>
public sealed record GetReceiptImageQuery(Guid Id) : IQuery<GetReceiptImageResponse>;
