using System.Globalization;
using System.Reflection;
using System.Text;
using TurnOva.Domain.Dispatch;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace TurnOva.API;

/// <summary>
/// Generates a searchable, vector PDF using embedded Noto fonts and HarfBuzz shaping.
/// The renderer fails closed for scripts that are not in the bundled Indic/Latin font set;
/// it never silently substitutes unsupported characters with question marks.
/// </summary>
public static class DriverPayslipPdf
{
    private const float PageWidth = 595;
    private const float PageHeight = 842;
    private const float Left = 50;
    private const float Right = 545;
    private const float ContentWidth = Right - Left;

    public static byte[] Generate(DriverMonthlyPayslip payslip)
    {
        ArgumentNullException.ThrowIfNull(payslip);
        if (payslip.Month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(payslip), "Payslip month must be between 1 and 12.");

        using var fonts = new FontCatalog();
        using var output = new MemoryStream();
        using var document = SKDocument.CreatePdf(output);
        var canvas = document.BeginPage(PageWidth, PageHeight);
        canvas.Clear(SKColors.White);

        DrawWatermark(canvas, fonts);
        DrawHeader(canvas, fonts);

        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(payslip.Month);
        var y = 148f;

        DrawText(canvas, fonts, "MONTHLY DRIVER PAYSLIP", Left, y, 18, Navy, bold: true);
        y += 28;
        DrawText(canvas, fonts, $"Pay period: {monthName} {payslip.Year}", Left, y, 10, Muted);
        y += 17;
        DrawText(canvas, fonts, $"Payslip ID: {payslip.PayslipId}", Left, y, 9, Muted);
        y += 30;

        DrawSection(canvas, fonts, "DRIVER AND EMPLOYER", ref y);
        DrawField(canvas, fonts, $"Driver: {payslip.DriverName}", ref y);
        DrawField(canvas, fonts, $"Driver ID: {payslip.DriverId}", ref y);
        DrawField(canvas, fonts, $"Union / Employer: {payslip.UnionName}", ref y);
        DrawField(canvas, fonts, $"Union ID: {payslip.UnionId}", ref y);
        y += 10;

        DrawSection(canvas, fonts, "MONTHLY WORK SUMMARY", ref y);
        DrawField(canvas, fonts, $"Total days worked: {payslip.DaysWorked}", ref y);
        DrawField(canvas, fonts, $"Rides accepted: {payslip.RidesAccepted}", ref y);
        DrawField(canvas, fonts, $"Rides completed: {payslip.RidesCompleted}", ref y);
        DrawField(canvas, fonts, $"Rides rejected: {payslip.RidesRejected}", ref y);
        y += 10;

        DrawSection(canvas, fonts, "PAYMENT INFORMATION", ref y);
        DrawField(canvas, fonts, "Compensation rules have not been configured.", ref y);
        DrawField(canvas, fonts, "Earnings, deductions and net payable are not calculated in this version.", ref y);
        y += 12;

        DrawField(canvas, fonts, $"Generated (UTC): {payslip.GeneratedAtUtc:yyyy-MM-dd HH:mm}", ref y);
        DrawWrappedText(
            canvas,
            fonts,
            "This PDF is the official downloadable format for this payslip.",
            Left,
            ref y,
            ContentWidth,
            9,
            Muted);

        using (var footerPaint = new SKPaint { Color = Border, StrokeWidth = 0.8f, IsAntialias = true })
            canvas.DrawLine(Left, 790, Right, 790, footerPaint);
        DrawText(canvas, fonts, "TurnOva · Cab driver work summary", Left, 808, 8, Muted);

        document.EndPage();
        document.Close();
        return output.ToArray();
    }

    private static readonly SKColor Navy = new(20, 42, 68);
    private static readonly SKColor Blue = new(20, 107, 190);
    private static readonly SKColor Teal = new(18, 166, 139);
    private static readonly SKColor Muted = new(101, 116, 132);
    private static readonly SKColor Border = new(220, 228, 236);

    private static void DrawHeader(SKCanvas canvas, FontCatalog fonts)
    {
        DrawCabMark(canvas, 50, 42, 0.85f, Blue, Teal);
        DrawText(canvas, fonts, "TurnOva", 104, 70, 24, Navy, bold: true);
        DrawText(canvas, fonts, "CAB DRIVER PAYSLIP", 106, 87, 8, Muted, bold: true);
        using var line = new SKPaint { Color = Border, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(Left, 112, Right, 112, line);
    }

    private static void DrawWatermark(SKCanvas canvas, FontCatalog fonts)
    {
        DrawCabMark(canvas, 247, 374, 2.6f, new SKColor(235, 242, 249), new SKColor(235, 248, 245));
        DrawText(canvas, fonts, "TurnOva", 174, 474, 58, new SKColor(239, 244, 249, 185), bold: true);
    }

    private static void DrawCabMark(SKCanvas canvas, float x, float y, float scale, SKColor bodyColor, SKColor accentColor)
    {
        using var body = new SKPaint { Color = bodyColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var glass = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var wheel = new SKPaint { Color = Navy.WithAlpha(220), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var accent = new SKPaint { Color = accentColor, IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawRoundRect(
            new SKRect(x + 2 * scale, y + 16 * scale, x + 58 * scale, y + 38 * scale),
            5 * scale,
            5 * scale,
            body);

        using var roof = new SKPath();
        roof.MoveTo(x + 13 * scale, y + 17 * scale);
        roof.LineTo(x + 22 * scale, y + 5 * scale);
        roof.LineTo(x + 43 * scale, y + 5 * scale);
        roof.LineTo(x + 51 * scale, y + 17 * scale);
        roof.Close();
        canvas.DrawPath(roof, body);

        canvas.DrawRoundRect(
            new SKRect(x + 23 * scale, y + 8 * scale, x + 41 * scale, y + 16 * scale),
            2 * scale,
            2 * scale,
            glass);
        canvas.DrawRoundRect(
            new SKRect(x + 15 * scale, y + 8 * scale, x + 21 * scale, y + 16 * scale),
            2 * scale,
            2 * scale,
            glass);

        canvas.DrawCircle(x + 15 * scale, y + 37 * scale, 5 * scale, wheel);
        canvas.DrawCircle(x + 45 * scale, y + 37 * scale, 5 * scale, wheel);
        canvas.DrawRoundRect(
            new SKRect(x + 4 * scale, y + 27 * scale, x + 11 * scale, y + 31 * scale),
            1.5f * scale,
            1.5f * scale,
            accent);
    }

    private static void DrawSection(SKCanvas canvas, FontCatalog fonts, string title, ref float y)
    {
        DrawText(canvas, fonts, title, Left, y, 11, Blue, bold: true);
        y += 8;
        using var line = new SKPaint { Color = Border, StrokeWidth = 0.7f, IsAntialias = true };
        canvas.DrawLine(Left, y, Right, y, line);
        y += 17;
    }

    private static void DrawField(SKCanvas canvas, FontCatalog fonts, string value, ref float y)
    {
        DrawWrappedText(canvas, fonts, value, Left, ref y, ContentWidth, 9.5f, Navy);
        y += 3;
    }

    private static void DrawWrappedText(
        SKCanvas canvas,
        FontCatalog fonts,
        string text,
        float x,
        ref float baseline,
        float maxWidth,
        float fontSize,
        SKColor color,
        bool bold = false)
    {
        foreach (var line in WrapText(fonts, text, maxWidth, fontSize, bold))
        {
            DrawText(canvas, fonts, line, x, baseline, fontSize, color, bold);
            baseline += fontSize + 4;
        }
    }

    private static IEnumerable<string> WrapText(
        FontCatalog fonts,
        string text,
        float maxWidth,
        float fontSize,
        bool bold)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return string.Empty;
            yield break;
        }

        var line = new StringBuilder();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? token : $"{line} {token}";
            if (MeasureText(fonts, candidate, fontSize, bold) <= maxWidth)
            {
                line.Clear();
                line.Append(candidate);
                continue;
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (MeasureText(fonts, token, fontSize, bold) <= maxWidth)
            {
                line.Append(token);
                continue;
            }

            foreach (var segment in SplitLongToken(fonts, token, maxWidth, fontSize, bold))
            {
                if (line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                line.Append(segment);
                if (MeasureText(fonts, line.ToString(), fontSize, bold) >= maxWidth)
                {
                    yield return line.ToString();
                    line.Clear();
                }
            }
        }

        if (line.Length > 0)
            yield return line.ToString();
    }

    private static IEnumerable<string> SplitLongToken(
        FontCatalog fonts,
        string token,
        float maxWidth,
        float fontSize,
        bool bold)
    {
        var part = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(token);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var candidate = part.ToString() + element;
            if (part.Length > 0 && MeasureText(fonts, candidate, fontSize, bold) > maxWidth)
            {
                yield return part.ToString();
                part.Clear();
            }

            part.Append(element);
        }

        if (part.Length > 0)
            yield return part.ToString();
    }

    private static float MeasureText(FontCatalog fonts, string text, float fontSize, bool bold)
    {
        var width = 0f;
        foreach (var run in fonts.SplitRuns(text))
        {
            var face = fonts.Get(run.Script);
            using var font = CreateFont(face, fontSize, bold);
            var shaped = face.Shaper.Shape(run.Text, font);
            EnsureGlyphCoverage(run, shaped.Codepoints);
            width += shaped.Width;
        }

        return width;
    }

    private static float DrawText(
        SKCanvas canvas,
        FontCatalog fonts,
        string text,
        float x,
        float baseline,
        float fontSize,
        SKColor color,
        bool bold = false)
    {
        var start = x;
        foreach (var run in fonts.SplitRuns(text))
        {
            var face = fonts.Get(run.Script);
            using var font = CreateFont(face, fontSize, bold);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var shaped = face.Shaper.Shape(run.Text, font);
            EnsureGlyphCoverage(run, shaped.Codepoints);
            canvas.DrawShapedText(
                face.Shaper,
                run.Text,
                x,
                baseline,
                SKTextAlign.Left,
                font,
                paint);
            x += shaped.Width;
        }

        return x - start;
    }

    private static SKFont CreateFont(FontFace face, float size, bool bold) =>
        new(face.Typeface, size)
        {
            Embolden = bold,
            LinearMetrics = true,
            Subpixel = true
        };

    private static void EnsureGlyphCoverage(ScriptRun run, IReadOnlyList<uint> glyphs)
    {
        if (glyphs.Any(glyph => glyph == 0))
        {
            throw new NotSupportedException(
                $"The bundled {run.Script} payslip font does not cover every character in the supplied name or label. " +
                "The PDF was not generated with replacement question marks.");
        }
    }

    private enum FontScript
    {
        Latin,
        Devanagari,
        Bengali,
        Gurmukhi,
        Gujarati,
        Oriya,
        Tamil,
        Telugu,
        Kannada,
        Malayalam
    }

    private readonly record struct ScriptRun(FontScript Script, string Text);

    private sealed class FontCatalog : IDisposable
    {
        private readonly Dictionary<FontScript, FontFace> _faces = new();

        public FontCatalog()
        {
            Load(FontScript.Latin, "NotoSansTelugu.ttf");
            Load(FontScript.Devanagari, "NotoSansDevanagari.ttf");
            Load(FontScript.Bengali, "NotoSansBengali.ttf");
            Load(FontScript.Gurmukhi, "NotoSansGurmukhi.ttf");
            Load(FontScript.Gujarati, "NotoSansGujarati.ttf");
            Load(FontScript.Oriya, "NotoSansOriya.ttf");
            Load(FontScript.Tamil, "NotoSansTamil.ttf");
            Load(FontScript.Telugu, "NotoSansTelugu.ttf");
            Load(FontScript.Kannada, "NotoSansKannada.ttf");
            Load(FontScript.Malayalam, "NotoSansMalayalam.ttf");
        }

        public FontFace Get(FontScript script) => _faces[script];

        public IEnumerable<ScriptRun> SplitRuns(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var current = FontScript.Latin;
            var value = new StringBuilder();

            foreach (var rune in text.EnumerateRunes())
            {
                var next = ResolveScript(rune, current);
                if (value.Length > 0 && next != current)
                {
                    yield return new ScriptRun(current, value.ToString());
                    value.Clear();
                }

                current = next;
                value.Append(rune.ToString());
            }

            if (value.Length > 0)
                yield return new ScriptRun(current, value.ToString());
        }

        private void Load(FontScript script, string fileName)
        {
            var resourceName = $"{typeof(DriverPayslipPdf).Namespace}.Fonts.{fileName}";
            var stream = typeof(DriverPayslipPdf).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Bundled payslip font resource '{resourceName}' is missing.");

            var typeface = SKTypeface.FromStream(stream);
            if (typeface is null)
            {
                stream.Dispose();
                throw new InvalidOperationException($"Bundled payslip font resource '{resourceName}' is not a valid font.");
            }

            try
            {
                _faces.Add(script, new FontFace(typeface));
            }
            catch
            {
                typeface.Dispose();
                throw;
            }
        }

        private static FontScript ResolveScript(Rune rune, FontScript current)
        {
            var value = rune.Value;
            if (value is >= 0x0900 and <= 0x097F) return FontScript.Devanagari;
            if (value is >= 0x0980 and <= 0x09FF) return FontScript.Bengali;
            if (value is >= 0x0A00 and <= 0x0A7F) return FontScript.Gurmukhi;
            if (value is >= 0x0A80 and <= 0x0AFF) return FontScript.Gujarati;
            if (value is >= 0x0B00 and <= 0x0B7F) return FontScript.Oriya;
            if (value is >= 0x0B80 and <= 0x0BFF) return FontScript.Tamil;
            if (value is >= 0x0C00 and <= 0x0C7F) return FontScript.Telugu;
            if (value is >= 0x0C80 and <= 0x0CFF) return FontScript.Kannada;
            if (value is >= 0x0D00 and <= 0x0D7F) return FontScript.Malayalam;

            var category = Rune.GetUnicodeCategory(rune);
            return category is
                UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark or
                UnicodeCategory.Format or
                UnicodeCategory.SpaceSeparator or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.DashPunctuation or
                UnicodeCategory.OpenPunctuation or
                UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or
                UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation
                ? current
                : FontScript.Latin;
        }

        public void Dispose()
        {
            foreach (var face in _faces.Values)
                face.Dispose();
            _faces.Clear();
        }
    }

    private sealed class FontFace : IDisposable
    {
        public SKTypeface Typeface { get; }
        public SKShaper Shaper { get; }

        public FontFace(SKTypeface typeface)
        {
            Typeface = typeface;
            try
            {
                Shaper = new SKShaper(typeface);
            }
            catch
            {
                typeface.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Shaper.Dispose();
            Typeface.Dispose();
        }
    }
}
