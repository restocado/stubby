using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence.Configurations;

internal sealed class FieldRecordConfiguration : IEntityTypeConfiguration<FieldRecord>
{
    public void Configure(EntityTypeBuilder<FieldRecord> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.FieldName).HasConversion<string>();

        var fieldNames = string.Join(", ", Enum.GetNames<FieldName>().Select(name => $"'{name}'"));
        builder.ToTable(t => t.HasCheckConstraint("CK_FieldRecords_FieldName", $"\"FieldName\" IN ({fieldNames})"));

        builder.HasIndex(f => new { f.Model, f.PromptVersion, f.FieldName })
            .HasFilter("\"IsManual\" = false");
    }
}
