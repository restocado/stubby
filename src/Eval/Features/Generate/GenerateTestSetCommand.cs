namespace Stubby.Eval.Features.Generate;

public sealed record GenerateTestSetCommand(GenerateOptions Options);

public sealed record GenerateTestSetResult(int ReceiptCount, string AnswersPath);
