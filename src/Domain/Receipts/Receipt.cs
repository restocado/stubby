using System.Globalization;

namespace Stubby.Domain.Receipts;

public sealed class Receipt
{
    public const int MaxFileSizeBytes = 10_485_760;

    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "application/pdf"];

    private Receipt() { }

    public Guid Id { get; private set; }
    public long TransactionNo { get; private set; }
    public ReceiptStatus Status { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public int FileSizeBytes { get; private set; }
    public string Checksum { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public int AttemptsUsed { get; private set; }
    public string? LastError { get; private set; }
    public string? ExtractedMerchant { get; private set; }
    public DateOnly? ExtractedDate { get; private set; }
    public decimal? ExtractedTotal { get; private set; }
    public string? Model { get; private set; }
    public string? PromptVersion { get; private set; }
    public bool IsFlagged { get; private set; }
    public string? FlagReasons { get; private set; }
    public string? Merchant { get; private set; }
    public DateOnly? ReceiptDate { get; private set; }
    public decimal? Total { get; private set; }
    public ReceiptCategory? Category { get; private set; }
    public bool IsManualEntry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    public bool CanBeOpened => Status is ReceiptStatus.ForReview or ReceiptStatus.Failed;

    public bool CanBeDeleted => Status is not (ReceiptStatus.Queued or ReceiptStatus.Processing);

    public static Receipt Create(
        string originalFileName,
        string contentType,
        int fileSizeBytes,
        string checksum,
        string imagePath,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileSizeBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fileSizeBytes, MaxFileSizeBytes);
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType));
        }

        return new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Queued,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Checksum = checksum,
            ImagePath = imagePath,
            AttemptsUsed = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void StartProcessing(DateTimeOffset now)
    {
        RequireStatus(nameof(StartProcessing), ReceiptStatus.Queued);
        Status = ReceiptStatus.Processing;
        UpdatedAt = now;
    }

    public void CompleteExtraction(
        string? merchant,
        DateOnly? date,
        decimal? total,
        string model,
        string promptVersion,
        bool isFlagged,
        string? flagReasons,
        DateTimeOffset now)
    {
        RequireStatus(nameof(CompleteExtraction), ReceiptStatus.Processing);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        ExtractedMerchant = string.IsNullOrWhiteSpace(merchant) ? null : merchant.Trim();
        ExtractedDate = date;
        ExtractedTotal = total;
        Model = model;
        PromptVersion = promptVersion;
        IsFlagged = isFlagged;
        FlagReasons = flagReasons;
        Status = ReceiptStatus.ForReview;
        UpdatedAt = now;
    }

    public void FailAttempt(string error, int maxAttempts, DateTimeOffset now)
    {
        RequireStatus(nameof(FailAttempt), ReceiptStatus.Processing);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        AttemptsUsed++;
        LastError = error;
        Status = AttemptsUsed < maxAttempts ? ReceiptStatus.Queued : ReceiptStatus.Failed;
        UpdatedAt = now;
    }

    public void ReturnToQueue(DateTimeOffset now)
    {
        RequireStatus(nameof(ReturnToQueue), ReceiptStatus.Processing);
        Status = ReceiptStatus.Queued;
        UpdatedAt = now;
    }

    public void Reprocess(DateTimeOffset now)
    {
        RequireStatus(nameof(Reprocess), ReceiptStatus.Failed);
        AttemptsUsed = 0;
        LastError = null;
        Status = ReceiptStatus.Queued;
        UpdatedAt = now;
    }

    public IReadOnlyList<FieldRecord> Approve(
        string? merchant,
        DateOnly? date,
        decimal? total,
        ReceiptCategory category,
        DateTimeOffset now)
    {
        RequireStatus(nameof(Approve), ReceiptStatus.ForReview, ReceiptStatus.Failed);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchant);
        if (date is null)
        {
            throw new ArgumentException("Date is required.", nameof(date));
        }

        if (total is null)
        {
            throw new ArgumentException("Total is required.", nameof(total));
        }

        if (decimal.Round(total.Value, 2) != total.Value)
        {
            throw new ArgumentException("Total has more than two decimals.", nameof(total));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentException($"Unknown category '{category}'.", nameof(category));
        }

        var isManual = Status == ReceiptStatus.Failed;
        var model = isManual ? null : Model;
        var promptVersion = isManual ? null : PromptVersion;
        var finalMerchant = merchant.Trim();
        var extractedMerchant = isManual ? null : ExtractedMerchant;
        var extractedDate = !isManual && ExtractedDate is { } d ? FormatDate(d) : null;
        var extractedTotal = !isManual && ExtractedTotal is { } t ? FormatTotal(t) : null;

        FieldRecord[] records =
        [
            FieldRecord.Create(
                FieldName.Merchant,
                extractedMerchant,
                finalMerchant,
                isManual,
                model,
                promptVersion,
                now),
            FieldRecord.Create(
                FieldName.Date,
                extractedDate,
                FormatDate(date.Value),
                isManual,
                model,
                promptVersion,
                now),
            FieldRecord.Create(
                FieldName.Total,
                extractedTotal,
                FormatTotal(total.Value),
                isManual,
                model,
                promptVersion,
                now),
        ];

        Merchant = finalMerchant;
        ReceiptDate = date;
        Total = total;
        Category = category;
        IsManualEntry = isManual;
        ApprovedAt = now;
        Status = ReceiptStatus.Approved;
        UpdatedAt = now;

        return records;
    }

    private void RequireStatus(string action, params ReceiptStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Cannot {action} a receipt that is {Status}.");
        }
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTotal(decimal total) =>
        total.ToString("F2", CultureInfo.InvariantCulture);
}
