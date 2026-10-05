using PolvorApp.SharedKernel.Fonts;
using PolvorApp.SharedKernel.Images;
using SkiaSharp;

namespace PolvorApp.Api.Platform.Images;

/// <summary>
/// Draws the seed's specimen license cards with SkiaSharp (realistic-seed-data, design D6): the
/// <see cref="SpecimenCardLayout"/> texts in the embedded Geist font (the runtime image has no system
/// fonts) over a warm-to-blue card, a red-yellow-red band on the front and the grid on the back. No
/// emblem is drawn. The seeder stores the PNG through the same normaliser as uploads.
/// </summary>
internal sealed class SkiaSpecimenCardPainter : ISpecimenCardPainter, IDisposable
{
    private static readonly SKColor Ink = new(0x1F, 0x24, 0x30);
    private static readonly SKColor LightInk = new(0xF4, 0xF6, 0xFB);
    private static readonly SKColor WatermarkInk = new(0xC0, 0x1C, 0x28, 0x5C);
    private static readonly SKColor StampInk = new(0xB0, 0x10, 0x1C);

    /// <summary>Below this a shrunk text would be unreadable: the layout is wrong, not the data.</summary>
    private const float MinTextSize = 12;
    private static readonly SKColor Red = new(0xC6, 0x0B, 0x1E);
    private static readonly SKColor Yellow = new(0xFF, 0xC4, 0x00);

    private readonly SKTypeface _regular = Load(EmbeddedFont.GeistRegular);
    private readonly SKTypeface _bold = Load(EmbeddedFont.GeistBold);

    public byte[] Front(SpecimenCardData card) => Render(SpecimenCardLayout.Front(card), front: true);

    public byte[] Back(SpecimenCardData card) => Render(SpecimenCardLayout.Back(card), front: false);

    public void Dispose()
    {
        _regular.Dispose();
        _bold.Dispose();
    }

    private byte[] Render(IReadOnlyList<SpecimenText> texts, bool front)
    {
        using var surface = SKSurface.Create(new SKImageInfo(SpecimenCardLayout.Width, SpecimenCardLayout.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("The specimen card surface could not be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        DrawCard(canvas, front);
        if (front)
        {
            DrawBand(canvas);
        }
        else
        {
            DrawGrid(canvas);
        }

        foreach (var text in texts)
        {
            DrawText(canvas, text);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The specimen card could not be encoded as PNG.");
        return data.ToArray();
    }

    private static void DrawCard(SKCanvas canvas, bool front)
    {
        SKColor[] colours = front
            ? [new(0xF2, 0xD7, 0xA6), new(0xF6, 0xE7, 0xCB), new(0x9D, 0xB7, 0xE8), new(0x34, 0x55, 0xAE)]
            : [new(0xF3, 0xDF, 0xA8), new(0xF4, 0xF0, 0xE4), new(0xC5, 0xD5, 0xF0), new(0x6F, 0x95, 0xDC)];
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(SpecimenCardLayout.Width, front ? 0 : SpecimenCardLayout.Height),
            colours,
            [0f, 0.45f, 0.75f, 1f],
            SKShaderTileMode.Clamp);
        using var fill = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(4, 4, SpecimenCardLayout.Width - 4, SpecimenCardLayout.Height - 4), 32, 32, fill);
    }

    /// <summary>A plain red-yellow-red band: colours only, no emblem.</summary>
    private static void DrawBand(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = false };
        paint.Color = Red;
        canvas.DrawRect(new SKRect(40, 48, 150, 60), paint);
        canvas.DrawRect(new SKRect(40, 84, 150, 96), paint);
        paint.Color = Yellow;
        canvas.DrawRect(new SKRect(40, 60, 150, 84), paint);
    }

    private static void DrawGrid(SKCanvas canvas)
    {
        using var line = new SKPaint { Color = Ink, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true };
        var columns = SpecimenCardLayout.GridColumns;
        var rows = SpecimenCardLayout.GridRows;
        canvas.DrawRect(new SKRect(columns[0], rows[0], columns[^1], rows[^1]), line);
        foreach (var y in rows[1..^1])
        {
            canvas.DrawLine(columns[0], y, columns[^1], y, line);
        }

        // The observations row (the last one) spans the whole width.
        foreach (var x in columns[1..^1])
        {
            canvas.DrawLine(x, rows[0], x, rows[^2], line);
        }
    }

    private void DrawText(SKCanvas canvas, SpecimenText text)
    {
        using var font = new SKFont(text.Bold ? _bold : _regular, text.Size) { Subpixel = true };

        // Skia would draw a missing letter as an empty box: fail like the PDFs do instead.
        if (!font.ContainsGlyphs(text.Text))
        {
            throw new InvalidOperationException("A specimen card text has a letter the embedded font lacks.");
        }

        if (text.MaxWidth > 0)
        {
            var width = font.MeasureText(text.Text);
            if (width > text.MaxWidth)
            {
                font.Size = text.Size * text.MaxWidth / width;
                if (font.Size < MinTextSize)
                {
                    throw new InvalidOperationException($"A specimen card text needs {font.Size:0.#} px to fit; the minimum is {MinTextSize} px.");
                }
            }
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = text.Ink switch
            {
                SpecimenInk.Light => LightInk,
                SpecimenInk.Watermark => WatermarkInk,
                SpecimenInk.Stamp => StampInk,
                _ => Ink,
            },
        };
        var align = text.Align switch
        {
            SpecimenAlign.Center => SKTextAlign.Center,
            SpecimenAlign.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left,
        };
        canvas.Save();
        canvas.Translate(text.X, text.Y);
        canvas.RotateDegrees(text.Rotation);
        canvas.DrawText(text.Text, 0, 0, align, font, paint);
        canvas.Restore();
    }

    /// <summary>Copies the font into Skia's own buffer first: a typeface may read its source lazily.</summary>
    private static SKTypeface Load(EmbeddedFont font)
    {
        using var stream = EmbeddedFonts.Open(font);
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException($"The embedded font {font} could not be loaded.");
    }
}
