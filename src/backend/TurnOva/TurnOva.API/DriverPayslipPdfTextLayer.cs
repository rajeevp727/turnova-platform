using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf.IO;

namespace TurnOva.API;

/// <summary>
/// Source text and layout coordinates for the searchable layer appended to the
/// HarfBuzz-shaped visual PDF.
/// </summary>
internal sealed record PayslipTextPlacement(
    string Text,
    float X,
    float Baseline,
    float FontSize,
    string FontFamily);

/// <summary>
/// Appends a near-transparent, embedded Unicode text layer to the visually shaped
/// Skia PDF. Skia's PDF backend can outline positioned glyph blobs; this layer keeps
/// the original text searchable/selectable without changing the visible layout.
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

    public static byte[] Append(byte[] sourcePdf, IReadOnlyList<PayslipTextPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(sourcePdf);
        ArgumentNullException.ThrowIfNull(placements);
        if (placements.Count == 0)
            return sourcePdf;

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
            // One alpha unit makes the white text effectively invisible on the
            // white page while preserving actual Unicode PDF text operators.
            var brush = new XSolidBrush(XColor.FromArgb(1, 255, 255, 255));
            var fonts = new Dictionary<string, XFont>(StringComparer.OrdinalIgnoreCase);

            foreach (var placement in placements)
            {
                if (!fonts.TryGetValue(placement.FontFamily, out var font))
                {
                    font = new XFont(
                        placement.FontFamily,
                        placement.FontSize,
                        XFontStyle.Regular,
                        XPdfFontOptions.UnicodeDefault);
                    fonts.Add(placement.FontFamily, font);
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

    private static bool RegisterFontResolver()
    {
        GlobalFontSettings.FontResolver = new NotoFontResolver();
        return true;
    }

    private sealed class NotoFontResolver : IFontResolver
    {
        public string DefaultFontName => "Noto Sans Telugu";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (!FontFiles.TryGetValue(familyName, out var fileName))
                return null!;

            // The searchable layer is invisible; the visual bold/weight treatment
            // remains in the Skia-rendered page. A single licensed regular face is
            // sufficient for Unicode extraction.
            return new FontResolverInfo(fileName);
        }

        public byte[] GetFont(string faceName)
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
