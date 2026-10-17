using System.Globalization;
using Stubby.Eval.Features.Generate;

return args switch
{
    ["generate", .. var options] => Generate(options),
    ["run", ..] => NotBuiltYet("run"),
    _ => Usage(),
};

static int Generate(string[] options)
{
    var folder = "testset";
    var seed = GenerateOptions.DefaultSeed;
    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--out" when i + 1 < options.Length:
                folder = options[++i];
                break;
            case "--seed" when i + 1 < options.Length && int.TryParse(options[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                seed = parsed;
                i++;
                break;
            default:
                return Usage();
        }
    }

    var result = new GenerateTestSetCommandHandler().Handle(new GenerateTestSetCommand(new GenerateOptions(folder, seed)));
    Console.WriteLine($"Wrote {result.ReceiptCount} receipts and {Path.GetFileName(result.AnswersPath)} to {Path.GetFullPath(folder)} (seed {seed}).");
    return 0;
}

static int NotBuiltYet(string command)
{
    Console.Error.WriteLine($"The '{command}' command is not built yet.");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("""
        Stubby eval tool.

        Usage:
          generate [--out <folder>] [--seed <number>]   Write the 30 invented receipts and answers.json (default folder: testset).
          run                                           Score the extractor on the test set (not built yet).
        """);
    return 2;
}
