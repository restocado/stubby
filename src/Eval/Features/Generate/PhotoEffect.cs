using SkiaSharp;
using Stubby.Eval.Features.Generate.Content;

namespace Stubby.Eval.Features.Generate;

/// <summary>
/// Makes a clean render look like a phone photo of a receipt on a desk: tilted, soft, dim, glared on or smeared,
/// with sensor noise. All of it comes from one seeded generator, so the same seed gives the same picture.
/// The effects are kept mild enough for a person to read every line.
/// </summary>
internal static class PhotoEffect
{
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.None);

    public static SKBitmap Apply(SKBitmap receipt, PhotoStyle style, int seed)
    {
        var random = new Random(seed);
        var (angle, sigmaX, sigmaY, brightness, glare, noise) = style switch
        {
            PhotoStyle.TiltBlur => (4f, 0.9f, 0.9f, 1.00f, false, 6),
            PhotoStyle.LowLight => (-3f, 0.6f, 0.6f, 0.55f, false, 20),
            PhotoStyle.GlareTilt => (7f, 0.5f, 0.5f, 1.00f, true, 8),
            PhotoStyle.MotionBlur => (-2f, 1.5f, 0.2f, 1.00f, false, 10),
            _ => (0f, 0f, 0f, 1.00f, false, 0),
        };

        // The receipt is softened on its own, so the desk behind it stays sharp-edged like a real photo.
        using var soft = Soften(receipt, sigmaX, sigmaY);

        var width = receipt.Width + 220;
        var height = receipt.Height + 260;
        var photo = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(photo))
        {
            DrawDesk(canvas, width, height);

            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees(angle);
            canvas.Translate(-receipt.Width / 2f, -receipt.Height / 2f);
            using var shadow = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateDropShadow(5, 9, 7, 7, SKColors.Black.WithAlpha(170)),
            };
            using var softImage = SKImage.FromBitmap(soft);
            canvas.DrawImage(softImage, 0, 0, Smooth, shadow);

            if (glare)
            {
                DrawGlare(canvas, receipt.Width, receipt.Height);
            }
        }

        if (brightness < 1f)
        {
            Dim(photo, brightness);
        }

        AddNoise(photo, random, noise);
        return photo;
    }

    private static SKBitmap Soften(SKBitmap receipt, float sigmaX, float sigmaY)
    {
        var result = new SKBitmap(receipt.Width, receipt.Height);
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigmaX, sigmaY) };
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(receipt);
        canvas.DrawImage(image, 0, 0, Smooth, paint);
        return result;
    }

    private static void DrawDesk(SKCanvas canvas, int width, int height)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [new SKColor(66, 58, 50), new SKColor(34, 31, 29)],
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
    }

    // A soft white patch over one corner, as when a lamp or a window reflects off the paper.
    private static void DrawGlare(SKCanvas canvas, int width, int height)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.82f, height * 0.18f),
            Math.Max(width, height) * 0.42f,
            [SKColors.White.WithAlpha(205), SKColors.White.WithAlpha(0)],
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
    }

    private static void Dim(SKBitmap bitmap, float brightness)
    {
        var pixels = bitmap.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];

            // Slightly cooler than it is dark, as a dim room is.
            pixels[i] = new SKColor(
                (byte)(p.Red * brightness * 0.95f),
                (byte)(p.Green * brightness),
                (byte)(p.Blue * brightness * 1.05f > 255 ? 255 : p.Blue * brightness * 1.05f));
        }

        bitmap.Pixels = pixels;
    }

    private static void AddNoise(SKBitmap bitmap, Random random, int amount)
    {
        if (amount == 0)
        {
            return;
        }

        var pixels = bitmap.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            // Two uniform draws make a bell-shaped noise.
            var n = random.Next(-amount, amount + 1) + random.Next(-amount, amount + 1);
            var p = pixels[i];
            pixels[i] = new SKColor(Clamp(p.Red + n), Clamp(p.Green + n), Clamp(p.Blue + n));
        }

        bitmap.Pixels = pixels;
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
