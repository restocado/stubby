namespace Stubby.Domain.Receipts;

public sealed class FieldRecord
{
    private FieldRecord() { }

    public Guid Id { get; private set; }
    public FieldName FieldName { get; private set; }
    public string? ExtractedValue { get; private set; }
    public string FinalValue { get; private set; } = string.Empty;
    public bool IsManual { get; private set; }
    public string? Model { get; private set; }
    public string? PromptVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal static FieldRecord Create(
        FieldName fieldName,
        string? extractedValue,
        string finalValue,
        bool isManual,
        string? model,
        string? promptVersion,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            FieldName = fieldName,
            ExtractedValue = extractedValue,
            FinalValue = finalValue,
            IsManual = isManual,
            Model = model,
            PromptVersion = promptVersion,
            CreatedAt = now,
        };
}
