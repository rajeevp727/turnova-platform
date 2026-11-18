using System.Text;
using TurnOva.API;
using TurnOva.Domain.Dispatch;
using UglyToad.PdfPig;

namespace TurnOva.Tests;

public sealed class DriverPayslipPdfTests
{
    [Fact]
    public void GenerateRendersLatinAndNineIndicScriptsAsVectorGlyphs()
    {
        var payslip = CreatePayslip(
            "Raja రాజా ராசா ಕನ್ನಡ മലയാളം राजेश বাংলা ગુજરાતી ਪੰਜਾਬੀ ଓଡ଼ିଆ",
            "TurnOva తెలుగు தமிழ் ಕನ್ನಡ മലയാളം");

        var pdf = DriverPayslipPdf.Generate(payslip);
        var pdfText = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", pdfText, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF", pdfText.TrimEnd(), StringComparison.Ordinal);
        Assert.True(pdf.Length > 5_000, "The PDF should contain shaped vector glyphs for all bundled scripts.");
    }

    [Fact]
    public void PdfSharpBundledSegoeFontAssemblyIsNotRedistributed()
    {
        var bundledSegoeAssembly = Path.Combine(AppContext.BaseDirectory, "PdfSharp.WPFonts.dll");
        Assert.False(File.Exists(bundledSegoeAssembly));
    }

    [Fact]
    public void GeneratedPdfEmbedsFontsAndPreservesSearchableTextForAllBundledScripts()
    {
        var payslip = CreatePayslip(
            "Raja राजेश বাংলা ਪੰਜਾਬੀ ગુજરાતી ଓଡ଼ିଆ தமிழ் తెలుగు ಕನ್ನಡ മലയാളം",
            "TurnOva తెలుగు தமிழ்");

        var pdf = DriverPayslipPdf.Generate(payslip);
        var pdfStructure = Encoding.Latin1.GetString(pdf);

        Assert.Contains("/ToUnicode", pdfStructure, StringComparison.Ordinal);
        Assert.True(
            pdfStructure.Contains("/FontFile2", StringComparison.Ordinal) ||
            pdfStructure.Contains("/FontFile3", StringComparison.Ordinal),
            "The PDF must embed its Unicode font program.");

        using var stream = new MemoryStream(pdf);
        using var document = PdfDocument.Open(stream);
        Assert.Equal(1, document.NumberOfPages);

        var extracted = document.GetPage(1).Text;
        Assert.Contains("Raja", extracted, StringComparison.Ordinal);
        Assert.Contains("राजेश", extracted, StringComparison.Ordinal);
        Assert.Contains("বাংলা", extracted, StringComparison.Ordinal);
        Assert.Contains("ਪੰਜਾਬੀ", extracted, StringComparison.Ordinal);
        Assert.Contains("ગુજરાતી", extracted, StringComparison.Ordinal);
        Assert.Contains("ଓଡ଼ିଆ", extracted, StringComparison.Ordinal);
        Assert.Contains("தமிழ்", extracted, StringComparison.Ordinal);
        Assert.Contains("తెలుగు", extracted, StringComparison.Ordinal);
        Assert.Contains("ಕನ್ನಡ", extracted, StringComparison.Ordinal);
        Assert.Contains("മലയാളം", extracted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateUsesRequestIsolatedShapersWhenMultiplePayslipsRenderConcurrently()
    {
        var payslips = Enumerable.Range(0, 4)
            .Select(index => CreatePayslip(
                $"Driver-{index} রাজা தமிழ் ಕನ್ನಡ മലയാളം",
                $"Union-{index} हिंदी বাংলা"))
            .ToArray();
        var renders = payslips
            .Select(payslip => Task.Run(() => DriverPayslipPdf.Generate(payslip)))
            .ToArray();

        var documents = await Task.WhenAll(renders);

        for (var index = 0; index < documents.Length; index++)
        {
            var pdf = documents[index];
            var pdfHeader = Encoding.Latin1.GetString(pdf);
            Assert.StartsWith("%PDF-1.4", pdfHeader, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF", pdfHeader.TrimEnd(), StringComparison.Ordinal);
            Assert.Contains("/ToUnicode", pdfHeader, StringComparison.Ordinal);

            var extracted = ExtractText(pdf);
            Assert.Contains($"Driver-{index}", extracted, StringComparison.Ordinal);
            Assert.Contains("রাজা", extracted, StringComparison.Ordinal);
            Assert.Contains("தமிழ்", extracted, StringComparison.Ordinal);
            for (var other = 0; other < documents.Length; other++)
            {
                if (other == index)
                    continue;

                Assert.DoesNotContain($"Driver-{other}", extracted, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void GenerateFailsClosedForAnUnbundledScriptInsteadOfReplacingCharacters()
    {
        var payslip = CreatePayslip("Driver محمد", "Union One");

        var error = Assert.Throws<NotSupportedException>(() => DriverPayslipPdf.Generate(payslip));

        Assert.Contains("not generated with replacement question marks", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateWrapsLongUnicodeNamesWithoutChangingThePayslipSnapshot()
    {
        const string driverName = "Raja രാജാ ராசா ಕನ್ನಡ മലയാളം राजेश বাংলা ગુજરાતી ਪੰਜਾਬੀ ଓଡ଼ିଆ Raja TurnOva Driver";
        var payslip = CreatePayslip(driverName, "Union தமிழ் ಕನ್ನಡ");

        var pdf = DriverPayslipPdf.Generate(payslip);
        var pdfText = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", pdfText, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF", pdfText.TrimEnd(), StringComparison.Ordinal);
        Assert.True(pdf.Length > 5_000);

        var extracted = ExtractText(pdf);
        var normalizedExtracted = string.Concat(extracted.Where(character => !char.IsWhiteSpace(character)));
        var normalizedExpected = string.Concat(driverName.Where(character => !char.IsWhiteSpace(character)));
        Assert.Contains(normalizedExpected, normalizedExtracted, StringComparison.Ordinal);
        Assert.Equal(driverName, payslip.DriverName);
    }

    private static string ExtractText(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfDocument.Open(stream);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static DriverMonthlyPayslip CreatePayslip(string driverName, string unionName) =>
        new(
            "payslip-2026-10-driver-1",
            "driver-1",
            driverName,
            "union-1",
            unionName,
            2026,
            10,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(5.5)),
            21,
            40,
            38,
            2,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
}
