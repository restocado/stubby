using System.Globalization;
using System.Text.Json;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Extraction;

internal sealed record ParsedReceipt(string? Merchant, DateOnly? Date, decimal? Total, decimal? Subtotal, decimal? Tax);

/// <summary>
/// Turns the model's reply into fields. A field that is missing, null or unreadable is simply missing.
/// The reply fails as a whole only when it is not a JSON object, or when merchant, date and total are all missing.
/// </summary>
internal static class ExtractionResponseParser
{
    // The largest value that fits the numeric(15,2) columns.
    private const decimal MaxMoney = 9_999_999_999_999.99m;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ParsedReceipt Parse(string reply)
    {
        // The model may wrap the JSON in a markdown fence or add a sentence around it.
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new ExtractionException("The reply did not contain a JSON object.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(reply.AsMemory(start, end - start + 1), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ExtractionException("The reply was not valid JSON.", ex);
        }

        using (document)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                fields[property.Name] = property.Value;
            }

            var parsed = new ParsedReceipt(
                ReadText(fields, "merchant"),
                ReadDate(fields, "date"),
                ReadMoney(fields, "total"),
                ReadMoney(fields, "subtotal"),
                ReadMoney(fields, "tax"));

            if (parsed.Merchant is null && parsed.Date is null && parsed.Total is null)
            {
                throw new ExtractionException("The reply had no usable merchant, date or total.");
            }

            return parsed;
        }
    }

    private static string? ReadText(Dictionary<string, JsonElement> fields, string name)
    {
        if (fields.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    private static DateOnly? ReadDate(Dictionary<string, JsonElement> fields, string name)
    {
        var text = ReadText(fields, name);
        return text is not null
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ReadMoney(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var element))
        {
            return null;
        }

        decimal value;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                value = number;
                break;
            case JsonValueKind.String when TryParseMoneyText(element.GetString(), out var parsed):
                value = parsed;
                break;
            default:
                return null;
        }

        // Two decimals, as the database and the review form hold them.
        value = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(value) > MaxMoney ? null : value;
    }

    // Accepts "19.99", "$19.99" and "1,234.50", although the prompt asks for a plain number.
    private static bool TryParseMoneyText(string? text, out decimal value)
    {
        value = 0;
        if (text is null)
        {
            return false;
        }

        var cleaned = new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or ',' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
