using System.Globalization;
using Stubby.Domain.Receipts;

namespace Stubby.Web.Components;

/// <summary>How dates, money and categories are written on screen. No currency symbol: no currency is stored.</summary>
internal static class Format
{
    /// <summary>The shown amount, with thousands separators and two decimals: 3,697.54.</summary>
    public static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>The shown date: 14 Sep 2026.</summary>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Month(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Category(ReceiptCategory category) => category.ToString().ToLowerInvariant();

    /// <summary>The CSS variable that holds the category's colour.</summary>
    public static string CategoryColor(ReceiptCategory category) => $"var(--c-{Category(category)})";

    /// <summary>"1 receipt" or "3 receipts".</summary>
    public static string Receipts(int count) => count == 1 ? "1 receipt" : $"{count.ToString(CultureInfo.InvariantCulture)} receipts";
}
