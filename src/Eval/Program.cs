using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Application.Abstractions;
using Stubby.Eval.Features.Generate;
using Stubby.Eval.Features.Run;
using Stubby.Infrastructure;

return args switch
{
    ["generate", .. var options] => Generate(options),
    ["run", .. var options] => await RunAsync(options),
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

static async Task<int> RunAsync(string[] options)
{
    var testSet = "testset";
    var reportPath = "eval-report.md";
    for (var i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--testset" when i + 1 < options.Length:
                testSet = options[++i];
                break;
            case "--report" when i + 1 < options.Length:
                reportPath = options[++i];
                break;
            default:
                return Usage();
        }
    }

    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancel.Cancel();
    };

    try
    {
        // The web app's own settings and secrets, so the model, the limits and the API key are the app's.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExtractor(configuration);
        await using var provider = services.BuildServiceProvider();

        var gateway = provider.GetRequiredService<IAiGateway>();
        var limit = provider.GetRequiredService<IExtractionSettings>().FlagTotalLimit;
        var handler = new RunEvalCommandHandler(provider.GetRequiredService<IReceiptExtractor>());

        Console.WriteLine($"Reading the test set in {Path.GetFullPath(testSet)} with {gateway.Model}, one receipt at a time.");
        var report = await handler.Handle(
            new RunEvalCommand(new RunEvalOptions(testSet, limit, reportPath, gateway.Model)),
            cancel.Token,
            (done, total, result) => Console.WriteLine(
                $"[{done,2}/{total}] {result.Expected.File}: " +
                (result.Failed ? $"not read ({result.Failure})" : result.TotalCorrect && result.MerchantCorrect && result.DateCorrect ? "all correct" : "something is wrong")));

        Console.WriteLine();
        Console.WriteLine(report.ToMarkdown());
        Console.WriteLine($"The report is also in {Path.GetFullPath(reportPath)}.");
        return report.ExitCode;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("The run was stopped. No report was written.");
        return 2;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or InvalidDataException)
    {
        // A missing test set, or a missing or invalid setting. The message says which.
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

static int Usage()
{
    Console.Error.WriteLine("""
        Stubby eval tool. Run it from the repository root.

        Usage:
          generate [--out <folder>] [--seed <number>]
              Write the 30 invented receipts and answers.json (default folder: testset).

          run [--testset <folder>] [--report <file>]
              Read the test set through the extractor with the app's model and settings, score it, print a report
              and write it to a file (default: eval-report.md). The exit code is 0 when every target is met, 1 when
              one is missed, and 2 when the run could not be made. This sends the receipts to the model provider.
        """);
    return 2;
}
