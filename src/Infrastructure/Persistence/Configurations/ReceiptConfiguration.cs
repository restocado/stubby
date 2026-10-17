using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TransactionNo)
            .UseIdentityAlwaysColumn()
            .ValueGeneratedOnAdd();

        builder.Property(r => r.Status).HasConversion<string>();
        builder.Property(r => r.Category).HasConversion<string>();

        builder.Property(r => r.AttemptsUsed).HasDefaultValue(0);
        builder.Property(r => r.IsFlagged).HasDefaultValue(false);
        builder.Property(r => r.IsManualEntry).HasDefaultValue(false);

        builder.Property(r => r.ExtractedTotal).HasPrecision(15, 2);
        builder.Property(r => r.Total).HasPrecision(15, 2);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Receipts_Status", $"\"Status\" IN ({Quoted<ReceiptStatus>()})");
            t.HasCheckConstraint(
                "CK_Receipts_ContentType",
                "\"ContentType\" IN ('image/jpeg', 'image/png', 'application/pdf')");
            t.HasCheckConstraint(
                "CK_Receipts_FileSize",
                $"\"FileSizeBytes\" BETWEEN 1 AND {Receipt.MaxFileSizeBytes}");
            t.HasCheckConstraint(
                "CK_Receipts_Category",
                $"\"Category\" IS NULL OR \"Category\" IN ({Quoted<ReceiptCategory>()})");
            t.HasCheckConstraint(
                "CK_Receipts_Approved",
                "\"Status\" <> 'Approved' OR (\"Merchant\" IS NOT NULL AND \"ReceiptDate\" IS NOT NULL "
                + "AND \"Total\" IS NOT NULL AND \"Category\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL)");
            t.HasCheckConstraint(
                "CK_Receipts_Manual",
                "NOT \"IsManualEntry\" OR (\"ExtractedMerchant\" IS NULL AND \"ExtractedDate\" IS NULL "
                + "AND \"ExtractedTotal\" IS NULL)");
        });

        builder.HasIndex(r => r.TransactionNo).IsUnique();
        builder.HasIndex(r => r.Checksum).IsUnique();
        builder.HasIndex(r => new { r.CreatedAt, r.Id }).IsDescending(true, true);
        builder.HasIndex(r => r.CreatedAt)
            .HasDatabaseName("IX_Receipts_CreatedAt_Queued")
            .HasFilter("\"Status\" = 'Queued'");
        builder.HasIndex(r => new { r.ReceiptDate, r.Category })
            .HasFilter("\"Status\" = 'Approved'");
        builder.HasIndex(r => r.Id)
            .HasDatabaseName("IX_Receipts_Id_Failed")
            .HasFilter("\"Status\" = 'Failed'");
    }

    private static string Quoted<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"));
}
