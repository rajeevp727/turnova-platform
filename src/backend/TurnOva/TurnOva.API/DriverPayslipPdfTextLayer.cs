using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

namespace TurnOva.API;

internal sealed record PayslipTextPlacement(
    string Text,
    float X,
    float Baseline,
    float FontSize,
    string FontFamily);

/// <summary>
/// Appends a low-visibility Unicode text layer to the HarfBuzz-shaped Skia page.
/// PDFsharp embeds the bundled fonts and emits Unicode mappings for extraction.
/// </summary>
internal static class DriverPayslipPdfTextLayer
{
    private static readonly Lazy<bool> ResolverRegistration = new(
        RegisterFontResolver,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly IReadOnlyDictionary<string, string> FontFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Noto Sans Telugu"] = "NotoSansTelugu.ttf",
            ["Noto Sans Devanagari"] = "NotoSansDevanagari.ttf",
            ["Noto Sans Bengali"] = "NotoSansBengali.ttf",
            ["Noto Sans Gurmukhi"] = "NotoSansGurmukhi.ttf",
            ["Noto Sans Gujarati"] = "NotoSansGujarati.ttf",
            ["Noto Sans Oriya"] = "NotoSansOriya.ttf",
            ["Noto Sans Tamil"] = "NotoSansTamil.ttf",
            ["Noto Sans Kannada"] = "NotoSansKannada.ttf",
            ["Noto Sans Malayalam"] = "NotoSansMalayalam.ttf"
        };

    // PDFsharp font resolver/cache state is process-wide. Serialize only this
    // append phase; visible Skia/HarfBuzz shaping remains request-local and concurrent.
    private static readonly object AppendGate = new();

    public static byte[] Append(byte[] sourcePdf, IReadOnlyList<PayslipTextPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(sourcePdf);
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0)
            return sourcePdf;

        lock (AppendGate)
        {
            _ = ResolverRegistration.Value;

            using var input = new MemoryStream(sourcePdf, writable: false);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            if (document.Pages.Count != 1)
                throw new InvalidOperationException("A driver payslip must contain exactly one page.");

            var page = document.Pages[0];
            using (var graphics = XGraphics.FromPdfPage(
                page,
                XGraphicsPdfPageOptions.Append,
                XPageDirection.Downwards))
            {
                var brush = new XSolidBrush(XColor.FromArgb(1, 255, 255, 255));
                var fonts = new Dictionary<(string Family, float Size), XFont>();

                foreach (var placement in placements)
                {
                    var key = (placement.FontFamily, placement.FontSize);
                    if (!fonts.TryGetValue(key, out var font))
                    {
                        font = new XFont(
                            placement.FontFamily,
                            placement.FontSize,
                            XFontStyleEx.Regular,
                            XPdfFontOptions.UnicodeDefault);
                        fonts.Add(key, font);
                    }

                    graphics.DrawString(
                        placement.Text,
                        font,
                        brush,
                        new XPoint(placement.X, placement.Baseline),
                        XStringFormats.BaseLineLeft);
                }
            }

            using var output = new MemoryStream();
            document.Save(output);
            return output.ToArray();
        }
    }

    private static bool RegisterFontResolver()
    {
        GlobalFontSettings.FontResolver = new NotoFontResolver();
        return true;
    }

    private sealed class NotoFontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            if (!FontFiles.TryGetValue(familyName, out var fileName))
                return null;

            return new FontResolverInfo(fileName, mustSimulateBold: false, mustSimulateItalic: false);
        }

        public byte[]? GetFont(string faceName)
        {
            if (!FontFiles.Values.Contains(faceName, StringComparer.Ordinal))
                throw new InvalidOperationException("An unregistered payslip font was requested.");

            var resourceName = $"{typeof(DriverPayslipPdfTextLayer).Namespace}.Fonts.{faceName}";
            using var stream = typeof(DriverPayslipPdfTextLayer).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Bundled payslip font resource '{resourceName}' is missing.");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }
}
