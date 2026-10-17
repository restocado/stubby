using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stubby.Eval.Answers;

/// <summary>
/// The correct answers for the generated test set. The generator writes it, and the run command reads it.
/// "Today" is fixed here, so the date flags are the same on every run, whatever the real date.
/// </summary>
public sealed record AnswerFile(
    int Seed,
    DateOnly Today,
    decimal TotalLimit,
    IReadOnlyList<ReceiptAnswer> Receipts)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // LF everywhere, so the file is the same on every system.
        NewLine = "\n",
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";

    public static AnswerFile Parse(string json) =>
        JsonSerializer.Deserialize<AnswerFile>(json, JsonOptions) ?? throw new InvalidDataException("The answer file is empty.");
}

/// <summary>
/// What a correct reading of one receipt gives. A value that is not on the receipt is null: the date of the receipt
/// whose date is missing, and the subtotal and tax of a receipt that shows neither.
/// </summary>
public sealed record ReceiptAnswer(
    string File,
    string Group,
    string Layout,
    string Merchant,
    DateOnly? Date,
    decimal Total,
    decimal? Subtotal,
    decimal? Tax,
    bool ExpectedFlagged,
    IReadOnlyList<string> ExpectedFlagReasons);
