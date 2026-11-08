using System.Text;
using TurnOva.API;
using TurnOva.Domain.Dispatch;

namespace TurnOva.Tests;

public sealed class DriverPayslipPdfTests
{
    [Fact]
    public void GenerateEmbedsShapedFontsForLatinAndMajorIndicScripts()
    {
        var payslip = CreatePayslip(
            "Raja రాజా ராசா ಕನ್ನಡ മലയാളം राजेश বাংলা ગુજરાતી ਪੰਜਾਬੀ ଓଡ଼ିଆ",
            "TurnOva తెలుగు தமிழ் ಕನ್ನಡ മലയാളം");

        var pdf = DriverPayslipPdf.Generate(payslip);
        var pdfText = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-", pdfText, StringComparison.Ordinal);
        Assert.Contains("/ToUnicode", pdfText, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", pdfText, StringComparison.Ordinal);
        Assert.Contains("NotoSans", pdfText, StringComparison.Ordinal);
        Assert.True(pdf.Length > 100_000, "The PDF should contain embedded/subsetted font programs.");
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
        Assert.True(pdf.Length > 100_000);
        Assert.Equal(driverName, payslip.DriverName);
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
