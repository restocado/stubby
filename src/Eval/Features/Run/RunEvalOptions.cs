namespace Stubby.Eval.Features.Run;

/// <param name="TestSetFolder">The folder that holds the receipts and <c>answers.json</c>.</param>
/// <param name="FlagTotalLimit">The configured flag limit: the same setting the app uses.</param>
/// <param name="ReportPath">Where the report is also written, or null to only return it.</param>
/// <param name="ModelName">The configured model, used when no receipt could be read at all.</param>
public sealed record RunEvalOptions(
    string TestSetFolder,
    decimal FlagTotalLimit,
    string? ReportPath = null,
    string ModelName = "unknown");
