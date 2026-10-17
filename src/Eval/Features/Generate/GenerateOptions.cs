namespace Stubby.Eval.Features.Generate;

/// <param name="OutputFolder">Where the receipts and <c>answers.json</c> are written.</param>
/// <param name="Seed">The same seed gives the same receipts and answers. A different one gives different values.</param>
/// <param name="Today">
/// The date the date flags are judged against, written into the answer file. It is fixed, and not the real date, so the
/// expected flags do not change as time passes.
/// </param>
/// <param name="TotalLimit">The flag limit for a total, written into the answer file.</param>
public sealed record GenerateOptions(
    string OutputFolder,
    int Seed = GenerateOptions.DefaultSeed,
    DateOnly? Today = null,
    decimal TotalLimit = GenerateOptions.DefaultTotalLimit)
{
    public const int DefaultSeed = 20261017;

    public const decimal DefaultTotalLimit = 3000m;

    public static readonly DateOnly DefaultToday = new(2026, 10, 17);

    public DateOnly TodayOrDefault => Today ?? DefaultToday;
}
