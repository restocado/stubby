using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;

namespace Stubby.Eval.Features.Generate.Layouts;

internal enum Align { Left, Center, Right }

internal static class Fonts
{
    // Shared by every generation, and generations can run at the same time.
    private static readonly ConcurrentDictionary<(string Family, bool Bold), SKTypeface> Typefaces = [];

    public static SKFont Mono(float size, bool bold = false) => Make("Courier New", size, bold);

    public static SKFont Sans(float size, bool bold = false) => Make("Arial", size, bold);

    public static SKFont Serif(float size, bool bold = false) => Make("Times New Roman", size, bold);

    // The family is looked up on the machine, and the closest font is used when it is missing.
    private static SKFont Make(string family, float size, bool bold)
    {
        var typeface = Typefaces.GetOrAdd(
            (family, bold),
            key => SKTypeface.FromFamilyName(key.Family, key.Bold ? SKFontStyle.Bold : SKFontStyle.Normal));

        return new SKFont(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = false };
    }
}

/// <summary>
/// A page being drawn from the top down. Everything is recorded first and drawn at the end, so the page can be
/// as tall as its content turns out to be.
/// </summary>
internal sealed class Sheet : IDisposable
{
    private readonly SKPictureRecorder _recorder = new();
    private readonly SKCanvas _canvas;
    private readonly SKColor _ink;
    private readonly SKColor _paper;

    public Sheet(int width, int margin, SKColor? ink = null, SKColor? paper = null)
    {
        Width = width;
        Margin = margin;
        Y = margin;
        _ink = ink ?? new SKColor(20, 20, 20);
        _paper = paper ?? SKColors.White;
        _canvas = _recorder.BeginRecording(new SKRect(0, 0, width, 6000));
    }

    public int Width { get; }

    public int Margin { get; }

    /// <summary>The top of the next line.</summary>
    public float Y { get; private set; }

    public float Left => Margin;

    public float Right => Width - Margin;

    public float CenterX => Width / 2f;

    public static string Money(decimal amount, bool thousands) =>
        amount.ToString(thousands ? "N2" : "F2", CultureInfo.InvariantCulture);

    /// <summary>One line of text. The baseline sits one font size below the top of the line.</summary>
    public void Text(string text, SKFont font, Align align = Align.Left, float leading = 1.35f)
    {
        DrawText(text, font, align, Y + font.Size);
        Y += font.Size * leading;
    }

    /// <summary>Text on the left and an amount or value on the right, on the same line.</summary>
    public void Columns(string left, string right, SKFont font, float leading = 1.35f)
    {
        var baseline = Y + font.Size;
        DrawText(left, font, Align.Left, baseline);
        DrawText(right, font, Align.Right, baseline);
        Y += font.Size * leading;
    }

    /// <summary>Text placed at a given x on the current line, without moving down.</summary>
    public void TextAt(string text, SKFont font, float x, Align align = Align.Left)
    {
        using var paint = Paint();
        _canvas.DrawText(text, x, Y + font.Size, ToSkia(align), font, paint);
    }

    public void NextLine(SKFont font, float leading = 1.35f) => Y += font.Size * leading;

    public void Gap(float height) => Y += height;

    public void Rule(float thickness = 1f, bool dashed = false)
    {
        using var paint = Paint();
        paint.StrokeWidth = thickness;
        paint.IsStroke = true;
        if (dashed)
        {
            paint.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
        }

        _canvas.DrawLine(Left, Y + 4, Right, Y + 4, paint);
        Y += 10;
    }

    public void Box(float x, float top, float width, float height, float thickness = 1.5f)
    {
        using var paint = Paint();
        paint.IsStroke = true;
        paint.StrokeWidth = thickness;
        _canvas.DrawRect(x, top, width, height, paint);
    }

    public void Bars(float height, Random random)
    {
        using var paint = Paint();
        var x = Left + 20;
        while (x < Right - 20)
        {
            var barWidth = random.Next(1, 4);
            _canvas.DrawRect(x, Y, barWidth, height, paint);
            x += barWidth + random.Next(1, 4);
        }

        Y += height + 6;
    }

    /// <summary>Draws what was recorded onto a bitmap that is as tall as the content.</summary>
    public SKBitmap ToBitmap()
    {
        using var picture = _recorder.EndRecording();
        var bitmap = new SKBitmap(Width, (int)Math.Ceiling(Y) + Margin);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(_paper);
        canvas.DrawPicture(picture);
        return bitmap;
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _recorder.Dispose();
    }

    private void DrawText(string text, SKFont font, Align align, float baseline)
    {
        var x = align switch { Align.Left => Left, Align.Center => CenterX, _ => Right };
        using var paint = Paint();
        _canvas.DrawText(text, x, baseline, ToSkia(align), font, paint);
    }

    private SKPaint Paint() => new() { Color = _ink, IsAntialias = true };

    private static SKTextAlign ToSkia(Align align) => align switch
    {
        Align.Left => SKTextAlign.Left,
        Align.Center => SKTextAlign.Center,
        _ => SKTextAlign.Right,
    };
}
