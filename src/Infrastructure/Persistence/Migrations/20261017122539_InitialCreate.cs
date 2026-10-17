using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Stubby.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    ExtractedValue = table.Column<string>(type: "text", nullable: true),
                    FinalValue = table.Column<string>(type: "text", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    PromptVersion = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldRecords", x => x.Id);
                    table.CheckConstraint("CK_FieldRecords_FieldName", "\"FieldName\" IN ('Merchant', 'Date', 'Total')");
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionNo = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "text", nullable: false),
                    ImagePath = table.Column<string>(type: "text", nullable: false),
                    AttemptsUsed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    ExtractedMerchant = table.Column<string>(type: "text", nullable: true),
                    ExtractedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtractedTotal = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    PromptVersion = table.Column<string>(type: "text", nullable: true),
                    IsFlagged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    FlagReasons = table.Column<string>(type: "text", nullable: true),
                    Merchant = table.Column<string>(type: "text", nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    IsManualEntry = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.CheckConstraint("CK_Receipts_Approved", "\"Status\" <> 'Approved' OR (\"Merchant\" IS NOT NULL AND \"ReceiptDate\" IS NOT NULL AND \"Total\" IS NOT NULL AND \"Category\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Receipts_Category", "\"Category\" IS NULL OR \"Category\" IN ('Groceries', 'Dining', 'Transport', 'Utilities', 'Shopping', 'Health', 'Entertainment', 'Other')");
                    table.CheckConstraint("CK_Receipts_ContentType", "\"ContentType\" IN ('image/jpeg', 'image/png', 'application/pdf')");
                    table.CheckConstraint("CK_Receipts_FileSize", "\"FileSizeBytes\" BETWEEN 1 AND 10485760");
                    table.CheckConstraint("CK_Receipts_Manual", "NOT \"IsManualEntry\" OR (\"ExtractedMerchant\" IS NULL AND \"ExtractedDate\" IS NULL AND \"ExtractedTotal\" IS NULL)");
                    table.CheckConstraint("CK_Receipts_Status", "\"Status\" IN ('Queued', 'Processing', 'Failed', 'ForReview', 'Approved')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldRecords_Model_PromptVersion_FieldName",
                table: "FieldRecords",
                columns: new[] { "Model", "PromptVersion", "FieldName" },
                filter: "\"IsManual\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_Checksum",
                table: "Receipts",
                column: "Checksum",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CreatedAt_Id",
                table: "Receipts",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CreatedAt_Queued",
                table: "Receipts",
                column: "CreatedAt",
                filter: "\"Status\" = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_Id_Failed",
                table: "Receipts",
                column: "Id",
                filter: "\"Status\" = 'Failed'");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceiptDate_Category",
                table: "Receipts",
                columns: new[] { "ReceiptDate", "Category" },
                filter: "\"Status\" = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_TransactionNo",
                table: "Receipts",
                column: "TransactionNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldRecords");

            migrationBuilder.DropTable(
                name: "Receipts");
        }
    }
}
