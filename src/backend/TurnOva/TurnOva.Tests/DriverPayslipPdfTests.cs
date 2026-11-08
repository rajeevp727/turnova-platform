using System.Text;
using UglyToad.PdfPig;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

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
        var extractedText = ExtractText(pdf);

        Assert.StartsWith("%PDF-", pdfText, StringComparison.Ordinal);
        Assert.Contains("/ToUnicode", pdfText, StringComparison.Ordinal);
        Assert.Contains("/FontFile", pdfText, StringComparison.Ordinal);
        Assert.Contains("Raja", extractedText, StringComparison.Ordinal);
        Assert.Contains("రాజా", extractedText, StringComparison.Ordinal);
        Assert.Contains("ராசா", extractedText, StringComparison.Ordinal);
        Assert.Contains("ಕನ್ನಡ", extractedText, StringComparison.Ordinal);
        Assert.Contains("മലയാളം", extractedText, StringComparison.Ordinal);
        Assert.Contains("राजेश", extractedText, StringComparison.Ordinal);
        Assert.Contains("বাংলা", extractedText, StringComparison.Ordinal);
        Assert.Contains("ગુજરાતી", extractedText, StringComparison.Ordinal);
        Assert.Contains("ਪੰਜਾਬੀ", extractedText, StringComparison.Ordinal);
        Assert.Contains("ଓଡ଼ିଆ", extractedText, StringComparison.Ordinal);
        Assert.True(pdf.Length > 100_000, "The PDF should contain embedded/subsetted font programs.");
    }

    [Fact]
    public async Task GenerateUsesRequestIsolatedShapersWhenMultiplePayslipsRenderConcurrently()
    {
        var payslip = CreatePayslip("Raja రాజా தமிழ் ಕನ್ನಡ മലയാളം", "Union हिंदी বাংলা");
        var renders = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => DriverPayslipPdf.Generate(payslip)))
            .ToArray();

        var documents = await Task.WhenAll(renders);

        Assert.All(documents, pdf =>
        {
            var content = Encoding.Latin1.GetString(pdf);
            Assert.StartsWith("%PDF-", content, StringComparison.Ordinal);
            Assert.Contains("Raja", ExtractText(pdf), StringComparison.Ordinal);
            Assert.Contains("Total days worked: 21", ExtractText(pdf), StringComparison.Ordinal);
        });
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
        const string driverName = "Raja రాజా ராசா ಕನ್ನಡ മലയാളം राजेश বাংলা ગુજરાતી ਪੰਜਾਬੀ ଓଡ଼ିଆ Raja TurnOva Driver";
        var payslip = CreatePayslip(driverName, "Union தமிழ் ಕನ್ನಡ");

        var pdf = DriverPayslipPdf.Generate(payslip);

        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.Contains("Raja", ExtractText(pdf), StringComparison.Ordinal);
        Assert.Contains("Total days worked: 21", ExtractText(pdf), StringComparison.Ordinal);
        Assert.True(pdf.Length > 20_000);
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
