using System.Text.RegularExpressions;
using SkiaSharp;
using Stubby.Eval.Answers;
using Stubby.Eval.Features.Generate.Content;
using Stubby.Eval.Features.Generate.Layouts;

namespace Stubby.Eval.Features.Generate;

/// <summary>Writes the 30 invented receipts and their answer file. The same seed gives the same files.</summary>
public sealed partial class GenerateTestSetCommandHandler
{
    public const string AnswersFileName = "answers.json";

    public GenerateTestSetResult Handle(GenerateTestSetCommand command)
    {
        var options = command.Options;
        var folder = options.OutputFolder;
        Directory.CreateDirectory(folder);
        RemoveEarlierOutput(folder);

        var today = options.TodayOrDefault;
        var plan = TestSetPlan.Create(options.Seed, today);
        foreach (var receipt in plan)
        {
            File.WriteAllBytes(Path.Combine(folder, receipt.FileName), Render(receipt, options.Seed));
        }

        var answers = new AnswerFile(options.Seed, today, options.TotalLimit, [.. plan.Select(r => r.ToAnswer(today, options.TotalLimit))]);
        var answersPath = Path.Combine(folder, AnswersFileName);
        File.WriteAllText(answersPath, answers.ToJson(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new GenerateTestSetResult(plan.Count, answersPath);
    }

    private static byte[] Render(PlannedReceipt receipt, int seed)
    {
        var pages = ReceiptLayout.For(receipt.Layout).Render(receipt.Content, receipt.ItemsPerPage, seed + (receipt.Number * 1009));
        try
        {
            return receipt.Format switch
            {
                OutputFormat.Pdf => PdfWriter.Write(pages),
                OutputFormat.Png => Encode(pages[0], SKEncodedImageFormat.Png, 100),
                _ => EncodeJpeg(receipt, pages[0], seed),
            };
        }
        finally
        {
            pages.ToList().ForEach(page => page.Dispose());
        }
    }

    private static byte[] EncodeJpeg(PlannedReceipt receipt, SKBitmap page, int seed)
    {
        if (receipt.Photo == PhotoStyle.None)
        {
            return Encode(page, SKEncodedImageFormat.Jpeg, 90);
        }

        using var photo = PhotoEffect.Apply(page, receipt.Photo, seed + (receipt.Number * 7919));
        return Encode(photo, SKEncodedImageFormat.Jpeg, 72);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    // Only the files this command writes are removed, so a folder with other things in it is left alone.
    private static void RemoveEarlierOutput(string folder)
    {
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (name == AnswersFileName || GeneratedFile().IsMatch(name))
            {
                File.Delete(path);
            }
        }
    }

    [GeneratedRegex(@"^\d{2}-[a-z]+-[a-z]+\.(png|jpg|pdf)$")]
    private static partial Regex GeneratedFile();
}
