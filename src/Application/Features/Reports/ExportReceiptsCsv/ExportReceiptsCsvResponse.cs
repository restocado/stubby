namespace Stubby.Application.Features.Reports.ExportReceiptsCsv;

/// <param name="Content">The whole file: UTF-8 with a byte order mark, so that Excel opens accents correctly.</param>
public sealed record ExportReceiptsCsvResponse(byte[] Content, string FileName);
