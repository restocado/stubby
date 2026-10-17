using System.Globalization;

namespace Stubby.Domain.Receipts;

public static class TransactionNumber
{
    public static string Format(long transactionNo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(transactionNo, 1);
        return "TXN-" + transactionNo.ToString("D6", CultureInfo.InvariantCulture);
    }
}
