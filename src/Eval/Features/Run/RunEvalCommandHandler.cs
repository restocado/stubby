using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts.Flagging;
using Stubby.Eval.Answers;
using Stubby.Eval.Features.Generate;

namespace Stubby.Eval.Features.Run;

/// <summary>
/// Reads the test set through the extractor, one receipt at a time, and scores it. It uses no web app, database
/// or queue: only the extractor and the flag rules.
/// </summary>
public sealed class RunEvalCommandHandler(IReceiptExtractor extractor)
{
    /// <param name="onResult">Called after each receipt with its position, the total and its result, for progress.</param>
    public async Task<EvalReport> Handle(
        RunEvalCommand command,
        CancellationToken cancellationToken,
        Action<int, int, ReceiptResult>? onResult = null)
    {
        var options = command.Options;
        var answers = LoadAnswers(options.TestSetFolder);
        var results = new List<ReceiptResult>();
        string? model = null;
        string? promptVersion = null;

        // One at a time, so the gateway's rate limit and circuit breaker are not set off by the run itself.
        foreach (var answer in answers.Receipts)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(options.TestSetFolder, answer.File), cancellationToken);
            var result = await ScoreAsync(answer, bytes, answers.Today, options.FlagTotalLimit, cancellationToken);

            results.Add(result.Scored);
            model ??= result.Model;
            promptVersion ??= result.PromptVersion;
            onResult?.Invoke(results.Count, answers.Receipts.Count, result.Scored);
        }

        var report = new EvalReport(
            model ?? options.ModelName,
            promptVersion ?? "unknown",
            answers.Seed,
            answers.Today,
            options.FlagTotalLimit,
            results);

        if (options.ReportPath is not null)
        {
            await File.WriteAllTextAsync(options.ReportPath, report.ToMarkdown(), cancellationToken);
        }

        return report;
    }

    private static AnswerFile LoadAnswers(string folder)
    {
        var path = Path.Combine(folder, GenerateTestSetCommandHandler.AnswersFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"There is no {GenerateTestSetCommandHandler.AnswersFileName} in '{folder}'. Make the test set with the generate command first.",
                path);
        }

        return AnswerFile.Parse(File.ReadAllText(path));
    }

    // A failure on one receipt is scored as wrong and the run goes on. Only a cancellation ends the run.
    // The reason is the extraction's own short reason, which never holds receipt content.
    private async Task<(ReceiptResult Scored, string? Model, string? PromptVersion)> ScoreAsync(
        ReceiptAnswer answer, byte[] bytes, DateOnly today, decimal limit, CancellationToken cancellationToken)
    {
        try
        {
            var read = await extractor.ExtractAsync(new ReceiptFile(bytes, ContentType(answer.File)), cancellationToken);
            var flagged = ReceiptFlagEvaluator.Evaluate(read.Total, read.Subtotal, read.Tax, read.Date, limit, today).IsFlagged;
            return (EvalScorer.Score(answer, read, failure: null, flagged), read.Model, read.PromptVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = ex is ExtractionException ? ex.Message : $"An unexpected {ex.GetType().Name} stopped the reading.";
            return (EvalScorer.Score(answer, result: null, reason, flagged: false), null, null);
        }
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        var other => throw new InvalidDataException($"'{file}' has an unsupported extension '{other}'."),
    };
}
