using Mediator;

namespace Stubby.Application.Features.Extraction.ProcessNextReceipt;

/// <summary>Takes the oldest Queued receipt, reads it and records the outcome. One call handles one receipt.</summary>
public sealed record ProcessNextReceiptCommand : ICommand<ProcessNextReceiptResult>;
