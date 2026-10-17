using System.Globalization;
using Stubby.Eval.Answers;
using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Eval.Features.Generate.Content;

internal enum ReceiptGroup { Clean, NoTax, TipDiscount, MultiPagePdf, Blurry, OverLimit, DateFlag }

internal enum LayoutKind { Thermal, Cafe, Grocery, Invoice, Restaurant, Fuel }

internal enum OutputFormat { Png, Jpg, Pdf }

/// <summary>How a phone photo of the receipt goes wrong. None means a clean render.</summary>
internal enum PhotoStyle { None, TiltBlur, LowLight, GlareTilt, MotionBlur }

internal sealed record PlannedReceipt(
    int Number,
    ReceiptGroup Group,
    LayoutKind Layout,
    OutputFormat Format,
    PhotoStyle Photo,
    int ItemsPerPage,
    ReceiptContent Content,
    DateOnly? Date)
{
    public string Extension => Format switch { OutputFormat.Png => "png", OutputFormat.Jpg => "jpg", _ => "pdf" };

    public string FileName => string.Create(
        CultureInfo.InvariantCulture,
        $"{Number:D2}-{Group.ToString().ToLowerInvariant()}-{Layout.ToString().ToLowerInvariant()}.{Extension}");

    /// <summary>What a correct reading gives, with the flags the real rules give for it.</summary>
    public ReceiptAnswer ToAnswer(DateOnly today, decimal totalLimit)
    {
        var total = Content.Summary.Single(s => s.Kind == SummaryKind.Total).Amount;
        var subtotal = Content.Summary.FirstOrDefault(s => s.Kind == SummaryKind.Subtotal)?.Amount;
        var tax = Content.Summary.FirstOrDefault(s => s.Kind == SummaryKind.Tax)?.Amount;
        var flags = ReceiptFlagEvaluator.Evaluate(total, subtotal, tax, Date, totalLimit, today);

        return new ReceiptAnswer(
            FileName,
            Group.ToString(),
            Layout.ToString(),
            Content.Merchant,
            Date,
            total,
            subtotal,
            tax,
            flags.IsFlagged,
            flags.Reasons.Select(r => r.ToString()).ToList());
    }
}

/// <summary>
/// Plans the 30 receipts from one seed. Everything is invented. The same seed gives the same plan, because
/// every random choice is made in a fixed order from one generator.
/// </summary>
internal static class TestSetPlan
{
    private enum Kind { Cafe, Bakery, Books, Hardware, Pharmacy, Grocery, FishMarket, Fuel, Restaurant, Electronics }

    private enum TotalStyle { Tip, Discount, TenderAndChange, SavingsLine }

    private static readonly Dictionary<Kind, string[]> Businesses = new()
    {
        [Kind.Cafe] = ["Maple & Pine Cafe", "Juniper Bean Coffee", "The Copper Kettle", "Lark Street Espresso"],
        [Kind.Bakery] = ["Corner Bakery No. 9", "Golden Crust Bakehouse", "Rye & Wheat Bakery"],
        [Kind.Books] = ["Blue Heron Books", "Paper Moon Bookshop", "Inkwell & Quill"],
        [Kind.Hardware] = ["Northgate Hardware", "Ironwood Supply Co.", "Bolt & Bracket"],
        [Kind.Pharmacy] = ["Sunrise Pharmacy", "Greenleaf Chemist", "Harbor Health Pharmacy"],
        [Kind.Grocery] = ["Riverside Grocers", "Meadowlark Market", "Fernbrook Foods", "Tidewater Supermarket"],
        [Kind.FishMarket] = ["Harbor Fish Market", "Salt & Net Seafood"],
        [Kind.Fuel] = ["Quick Fuel Station", "Roadrunner Petrol", "Westbound Services"],
        [Kind.Restaurant] = ["Lantern Noodle House", "Olive Branch Taverna", "Saffron Table", "Little Dumpling Kitchen"],
        [Kind.Electronics] = ["Brightwire Electronics", "Voltage & Co.", "Anchorage Marine Supply"],
    };

    // name, lowest and highest price in cents
    private static readonly Dictionary<Kind, (string Name, int Min, int Max)[]> Catalog = new()
    {
        [Kind.Cafe] = [("Flat white", 380, 520), ("Latte", 400, 560), ("Cappuccino", 380, 540), ("Almond croissant", 320, 450), ("Banana bread", 300, 420), ("Orange juice", 280, 400), ("Iced tea", 250, 380), ("Cheese toastie", 450, 650)],
        [Kind.Bakery] = [("Sourdough loaf", 550, 750), ("Cinnamon roll", 300, 420), ("Baguette", 280, 380), ("Custard tart", 280, 400), ("Rye bread", 500, 700), ("Almond danish", 350, 480)],
        [Kind.Books] = [("Paperback novel", 1200, 1800), ("Notebook A5", 450, 750), ("Gel pens 5pk", 350, 550), ("Bookmark", 150, 300), ("Cookbook", 1800, 3200), ("Road atlas", 1400, 2200)],
        [Kind.Hardware] = [("Wood screws 100pk", 500, 900), ("Paint roller", 700, 1100), ("Masking tape", 250, 450), ("Work gloves", 900, 1500), ("Claw hammer", 1200, 2000), ("Spirit level", 1500, 2600), ("LED bulbs 4pk", 800, 1400), ("Extension lead", 1200, 2200)],
        [Kind.Pharmacy] = [("Vitamin C 60ct", 800, 1200), ("Bandages", 350, 550), ("Hand cream", 600, 900), ("Cough syrup", 700, 1100), ("Toothpaste", 300, 500), ("Sunscreen SPF50", 900, 1500)],
        [Kind.Grocery] = [("Bananas", 150, 300), ("Milk 2L", 280, 380), ("Eggs 12", 380, 580), ("Rice 5kg", 900, 1400), ("Olive oil", 700, 1200), ("Tomatoes", 300, 480), ("Bread loaf", 250, 400), ("Cheddar", 450, 700), ("Apples", 350, 550), ("Pasta 500g", 150, 280), ("Yoghurt", 200, 400), ("Coffee beans", 800, 1300)],
        [Kind.FishMarket] = [("Salmon fillet", 1500, 2200), ("Prawns 500g", 1300, 1900), ("Lemons x4", 200, 320), ("Cod fillet", 1200, 1700), ("Mussels 1kg", 700, 1100)],
        [Kind.Fuel] = [("Car wash", 600, 1000), ("Coffee", 220, 320), ("Snack bar", 150, 250), ("Windscreen wash", 400, 650)],
        [Kind.Restaurant] = [("Dan dan noodles", 1200, 1600), ("Pork dumplings", 800, 1100), ("Jasmine tea", 250, 400), ("Spring rolls", 600, 900), ("Fried rice", 1000, 1400), ("Sparkling water", 300, 450), ("Mango pudding", 600, 800)],
        [Kind.Electronics] = [("Marine winch", 180000, 240000), ("Battery bank", 110000, 150000), ("Cable set", 1500, 3500), ("Mounting kit", 2500, 4500)],
    };

    // A name and a variant make an item. Eight variants give every catalog room for the 40 items of the longest receipt.
    private static readonly string[] Variants = ["", " organic", " large", " family pack", " value", " premium", " mini", " twin pack"];
    private static readonly string[] Streets = ["Example Street", "Sample Road", "Placeholder Lane", "Fakeville Avenue", "Demo Quay", "Mock Boulevard", "Imaginary Court", "Test Junction", "Pretend Place"];
    private static readonly string[] Footers = ["THANK YOU", "COME AGAIN", "KEEP THIS RECEIPT", "HAVE A NICE DAY", "SEE YOU SOON"];
    private static readonly string[] TotalLabels = ["TOTAL", "TOTAL DUE", "AMOUNT DUE", "TOTAL TO PAY"];
    private static readonly string[] TaxLabels = ["TAX", "VAT", "GST", "SALES TAX"];
    private static readonly decimal[] TaxRates = [0.05m, 0.06m, 0.07m, 0.08m, 0.09m];

    // Day-and-month-first or month-first numbers are ambiguous when both are 12 or less, so they only get days over 12.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd MMM yyyy", "MMM d, yyyy", "dd.MM.yyyy", "MM/dd/yyyy", "d MMMM yyyy"];

    private static readonly LayoutKind[] CleanLayouts =
    [
        LayoutKind.Thermal, LayoutKind.Cafe, LayoutKind.Grocery, LayoutKind.Invoice, LayoutKind.Restaurant, LayoutKind.Fuel,
        LayoutKind.Thermal, LayoutKind.Cafe, LayoutKind.Grocery, LayoutKind.Invoice, LayoutKind.Restaurant, LayoutKind.Fuel,
        LayoutKind.Thermal, LayoutKind.Cafe,
    ];

    private static readonly Dictionary<LayoutKind, Kind[]> LayoutKinds = new()
    {
        [LayoutKind.Thermal] = [Kind.Cafe, Kind.Bakery, Kind.Pharmacy, Kind.Grocery],
        [LayoutKind.Cafe] = [Kind.Cafe, Kind.Bakery, Kind.Books],
        [LayoutKind.Grocery] = [Kind.Grocery, Kind.FishMarket, Kind.Pharmacy],
        [LayoutKind.Invoice] = [Kind.Hardware, Kind.Books],
        [LayoutKind.Restaurant] = [Kind.Restaurant],
        [LayoutKind.Fuel] = [Kind.Fuel],
    };

    public static IReadOnlyList<PlannedReceipt> Create(int seed, DateOnly today)
    {
        var generator = new Generator(new Random(seed), today);
        var plan = new List<PlannedReceipt>();

        // 1-14: clean, six layouts, mixed fonts and date formats. Four are PDFs.
        for (var i = 0; i < CleanLayouts.Length; i++)
        {
            var format = i is 1 or 4 or 7 or 10 ? OutputFormat.Pdf : i % 2 == 0 ? OutputFormat.Png : OutputFormat.Jpg;
            plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.Clean, CleanLayouts[i], format, withTax: true));
        }

        // 15-18: no tax shown. Two are PDFs.
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.NoTax, LayoutKind.Cafe, OutputFormat.Png, withTax: false));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.NoTax, LayoutKind.Thermal, OutputFormat.Jpg, withTax: false));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.NoTax, LayoutKind.Grocery, OutputFormat.Pdf, withTax: false));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.NoTax, LayoutKind.Invoice, OutputFormat.Pdf, withTax: false));

        // 19-22: a tip, a discount, or several lines that look like totals. Two are PDFs.
        plan.Add(generator.Tricky(plan.Count + 1, LayoutKind.Restaurant, OutputFormat.Png, TotalStyle.Tip));
        plan.Add(generator.Tricky(plan.Count + 1, LayoutKind.Grocery, OutputFormat.Jpg, TotalStyle.Discount));
        plan.Add(generator.Tricky(plan.Count + 1, LayoutKind.Thermal, OutputFormat.Pdf, TotalStyle.TenderAndChange));
        plan.Add(generator.Tricky(plan.Count + 1, LayoutKind.Cafe, OutputFormat.Pdf, TotalStyle.SavingsLine));

        // 23-24: PDFs of several pages. 18 items to a page makes 26 items two pages and 40 items three.
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.MultiPagePdf, LayoutKind.Grocery, OutputFormat.Pdf, withTax: true, itemCount: 26, itemsPerPage: 18));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.MultiPagePdf, LayoutKind.Invoice, OutputFormat.Pdf, withTax: true, itemCount: 40, itemsPerPage: 18));

        // 25-28: phone photos that are blurred, dim, glared on or smeared.
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.Blurry, LayoutKind.Thermal, OutputFormat.Jpg, withTax: true, photo: PhotoStyle.TiltBlur));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.Blurry, LayoutKind.Cafe, OutputFormat.Jpg, withTax: true, photo: PhotoStyle.LowLight));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.Blurry, LayoutKind.Grocery, OutputFormat.Jpg, withTax: true, photo: PhotoStyle.GlareTilt));
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.Blurry, LayoutKind.Restaurant, OutputFormat.Jpg, withTax: true, photo: PhotoStyle.MotionBlur));

        // 29: a total at or above the flag limit.
        plan.Add(generator.OverLimit(plan.Count + 1));

        // 30: no date printed anywhere.
        plan.Add(generator.Standard(plan.Count + 1, ReceiptGroup.DateFlag, LayoutKind.Thermal, OutputFormat.Jpg, withTax: true, printDate: false));

        return plan;
    }

    private sealed class Generator(Random random, DateOnly today)
    {
        private readonly Dictionary<Kind, int> _nextBusiness = [];
        private int _next;

        public PlannedReceipt Standard(
            int number, ReceiptGroup group, LayoutKind layout, OutputFormat format, bool withTax,
            int itemCount = 0, int itemsPerPage = 100, PhotoStyle photo = PhotoStyle.None, bool printDate = true)
        {
            var kind = PickKind(layout);
            var items = Items(kind, itemCount > 0 ? itemCount : random.Next(2, 7));
            var subtotal = items.Sum(i => i.Amount);
            var summary = new List<SummaryLine>();
            if (withTax)
            {
                var rate = TaxRates[random.Next(TaxRates.Length)];
                var tax = Tax(subtotal, rate);
                summary.Add(new SummaryLine("SUBTOTAL", subtotal, SummaryKind.Subtotal));
                summary.Add(new SummaryLine(TaxLabel(rate), tax, SummaryKind.Tax));
                summary.Add(new SummaryLine(TotalLabel(), subtotal + tax, SummaryKind.Total));
            }
            else
            {
                summary.Add(new SummaryLine(TotalLabel(), subtotal, SummaryKind.Total));
            }

            return Build(number, group, layout, format, photo, itemsPerPage, kind, items, summary, printDate, thousands: false);
        }

        public PlannedReceipt Tricky(int number, LayoutKind layout, OutputFormat format, TotalStyle style)
        {
            var kind = PickKind(layout);
            var items = Items(kind, random.Next(3, 7));
            var subtotal = items.Sum(i => i.Amount);
            var rate = TaxRates[random.Next(TaxRates.Length)];
            var summary = new List<SummaryLine> { new("SUBTOTAL", subtotal, SummaryKind.Subtotal) };

            switch (style)
            {
                case TotalStyle.Tip:
                    var tax = Tax(subtotal, rate);
                    var tip = Tax(subtotal, new[] { 0.15m, 0.18m, 0.20m }[random.Next(3)]);
                    summary.Add(new SummaryLine(TaxLabel(rate), tax, SummaryKind.Tax));
                    summary.Add(new SummaryLine("TIP", tip, SummaryKind.Tip));
                    summary.Add(new SummaryLine(TotalLabel(), subtotal + tax + tip, SummaryKind.Total));
                    break;
                case TotalStyle.Discount:
                    var discount = Tax(subtotal, 0.10m);
                    var taxAfter = Tax(subtotal - discount, rate);
                    summary.Add(new SummaryLine("DISCOUNT 10%", -discount, SummaryKind.Discount));
                    summary.Add(new SummaryLine(TaxLabel(rate), taxAfter, SummaryKind.Tax));
                    summary.Add(new SummaryLine(TotalLabel(), subtotal - discount + taxAfter, SummaryKind.Total));
                    break;
                case TotalStyle.TenderAndChange:
                    var tax2 = Tax(subtotal, rate);
                    var total = subtotal + tax2;
                    var tendered = Math.Ceiling(total / 10m) * 10m;
                    summary.Add(new SummaryLine(TaxLabel(rate), tax2, SummaryKind.Tax));
                    summary.Add(new SummaryLine(TotalLabel(), total, SummaryKind.Total));
                    summary.Add(new SummaryLine("AMOUNT TENDERED", tendered, SummaryKind.Other));
                    summary.Add(new SummaryLine("CHANGE", tendered - total, SummaryKind.Other));
                    break;
                default:
                    var tax3 = Tax(subtotal, rate);
                    summary.Add(new SummaryLine(TaxLabel(rate), tax3, SummaryKind.Tax));
                    summary.Add(new SummaryLine(TotalLabel(), subtotal + tax3, SummaryKind.Total));
                    summary.Add(new SummaryLine("TOTAL SAVINGS", random.Next(150, 900) / 100m, SummaryKind.Other));
                    break;
            }

            return Build(number, ReceiptGroup.TipDiscount, layout, format, PhotoStyle.None, 100, kind, items, summary, printDate: true, thousands: false);
        }

        public PlannedReceipt OverLimit(int number)
        {
            var items = new List<LineItem>
            {
                Big("Marine winch", random.Next(180000, 240001)),
                Big("Battery bank", random.Next(110000, 150001)),
                Big("Cable set", random.Next(1500, 3501)),
            };
            var subtotal = items.Sum(i => i.Amount);
            var rate = TaxRates[random.Next(TaxRates.Length)];
            var tax = Tax(subtotal, rate);
            var summary = new List<SummaryLine>
            {
                new("SUBTOTAL", subtotal, SummaryKind.Subtotal),
                new(TaxLabel(rate), tax, SummaryKind.Tax),
                new(TotalLabel(), subtotal + tax, SummaryKind.Total),
            };

            // Large amounts are printed with thousands separators, as they are on a real receipt.
            return Build(number, ReceiptGroup.OverLimit, LayoutKind.Invoice, OutputFormat.Png, PhotoStyle.None, 100, Kind.Electronics, items, summary, printDate: true, thousands: true);
        }

        private static LineItem Big(string name, int cents) => new(name, cents / 100m);

        private PlannedReceipt Build(
            int number, ReceiptGroup group, LayoutKind layout, OutputFormat format, PhotoStyle photo, int itemsPerPage,
            Kind kind, IReadOnlyList<LineItem> items, IReadOnlyList<SummaryLine> summary, bool printDate, bool thousands)
        {
            var business = NextBusiness(kind);
            DateOnly? date = null;
            string? dateText = null;
            if (printDate)
            {
                var chosen = PickDate(layout);
                date = chosen.Date;
                dateText = chosen.Text;
            }

            var content = new ReceiptContent(
                business,
                [$"{random.Next(2, 400)} {Streets[random.Next(Streets.Length)]}", $"Tel 555-01{random.Next(0, 100):D2}"],
                dateText,
                $"{random.Next(7, 22):D2}:{random.Next(0, 60):D2}",
                random.Next(1000, 10000).ToString(CultureInfo.InvariantCulture),
                items,
                summary,
                [Footers[random.Next(Footers.Length)], Footers[random.Next(Footers.Length)]],
                thousands);

            return new PlannedReceipt(number, group, layout, format, photo, itemsPerPage, content, date);
        }

        private Kind PickKind(LayoutKind layout)
        {
            var kinds = LayoutKinds[layout];
            return kinds[_next++ % kinds.Length];
        }

        private string NextBusiness(Kind kind)
        {
            var names = Businesses[kind];
            _nextBusiness.TryGetValue(kind, out var index);
            _nextBusiness[kind] = index + 1;
            return names[index % names.Length];
        }

        // Distinct items: a name and a variant that has not been used yet.
        private List<LineItem> Items(Kind kind, int count)
        {
            var catalog = Catalog[kind];
            if (count > catalog.Length * Variants.Length)
            {
                throw new InvalidOperationException($"{kind} has fewer than {count} distinct items.");
            }

            var used = new HashSet<string>();
            var items = new List<LineItem>();
            while (items.Count < count)
            {
                var (name, min, max) = catalog[random.Next(catalog.Length)];
                var variant = Variants[random.Next(Variants.Length)];
                if (!used.Add(name + variant))
                {
                    continue;
                }

                var unit = random.Next(min, max + 1) / 100m;
                var quantity = random.Next(10) < 7 ? 1 : random.Next(2, 4);
                items.Add(new LineItem(
                    name + variant,
                    unit * quantity,
                    quantity > 1 ? string.Create(CultureInfo.InvariantCulture, $"{quantity} x {unit:F2}") : null));
            }

            if (kind == Kind.Fuel)
            {
                var litres = random.Next(150, 500) / 10m;
                var price = random.Next(149, 200) / 100m;
                var fuel = random.Next(2) == 0 ? "Unleaded" : "Diesel";
                items.Insert(0, new LineItem(fuel, decimal.Round(litres * price, 2, MidpointRounding.AwayFromZero),
                    string.Create(CultureInfo.InvariantCulture, $"{litres:F1} L @ {price:F2}")));
                items.RemoveAt(items.Count - 1);
            }

            return items;
        }

        private (DateOnly Date, string Text) PickDate(LayoutKind layout)
        {
            var format = DateFormats[((int)layout + random.Next(DateFormats.Length)) % DateFormats.Length];
            var numeric = format is "dd.MM.yyyy" or "MM/dd/yyyy";
            while (true)
            {
                var date = today.AddDays(-random.Next(2, 300));
                if (numeric && date.Day <= 12)
                {
                    continue;
                }

                return (date, date.ToString(format, CultureInfo.InvariantCulture));
            }
        }

        private string TotalLabel() => TotalLabels[random.Next(TotalLabels.Length)];

        private string TaxLabel(decimal rate) =>
            string.Create(CultureInfo.InvariantCulture, $"{TaxLabels[random.Next(TaxLabels.Length)]} {rate * 100:0.#}%");

        private static decimal Tax(decimal amount, decimal rate) => decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
    }
}
