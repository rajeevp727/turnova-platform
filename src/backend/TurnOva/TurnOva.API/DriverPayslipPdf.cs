using System.Globalization;
using System.Text;
using TurnOva.Domain.Dispatch;

namespace TurnOva.API;

public static class DriverPayslipPdf
{
    public static byte[] Generate(DriverMonthlyPayslip payslip)
    {
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(payslip.Month);
        var lines = new[]
        {
            "MONTHLY DRIVER PAYSLIP",
            $"Pay period: {monthName} {payslip.Year}",
            $"Payslip ID: {payslip.PayslipId}",
            "",
            "DRIVER AND EMPLOYER",
            $"Driver: {payslip.DriverName}",
            $"Driver ID: {payslip.DriverId}",
            $"Union / Employer: {payslip.UnionName}",
            $"Union ID: {payslip.UnionId}",
            "",
            "MONTHLY WORK SUMMARY",
            $"Total days worked: {payslip.DaysWorked}",
            $"Rides accepted: {payslip.RidesAccepted}",
            $"Rides completed: {payslip.RidesCompleted}",
            $"Rides rejected: {payslip.RidesRejected}",
            "",
            "PAYMENT INFORMATION",
            "Compensation rules have not been configured.",
            "Earnings, deductions and net payable are not calculated in this version.",
            "",
            $"Generated (UTC): {payslip.GeneratedAtUtc:yyyy-MM-dd HH:mm}",
            "This PDF is the official downloadable format for this payslip."
        };

        var commands = new StringBuilder();

        // Low-contrast, page-level TurnOva brand watermark behind the payslip content.
        DrawBrandMark(commands, 205, 385, 2.7, "0.94 0.97 1", "0.92 0.99 0.97");
        commands.Append("BT /F2 66 Tf 0.94 0.96 0.98 rg 170 330 Td (TurnOva) Tj ET\n");

        // Brand mark and company name in the document header.
        DrawBrandMark(commands, 50, 770, 0.62, "0.03 0.42 0.91", "0.05 0.82 0.68");
        commands.Append("BT /F2 23 Tf 0.04 0.13 0.25 rg 91 782 Td (TurnOva) Tj ET\n");
        commands.Append("BT /F1 9 Tf 0.35 0.42 0.50 rg 92 766 Td (MOBILITY PLATFORM) Tj ET\n");

        var y = 728;
        foreach (var line in lines)
        {
            var fontSize = line switch
            {
                "MONTHLY DRIVER PAYSLIP" => 16,
                "DRIVER AND EMPLOYER" or "MONTHLY WORK SUMMARY" or "PAYMENT INFORMATION" => 12,
                _ => 10
            };
            var font = fontSize == 16 || fontSize == 12 ? "/F2" : "/F1";
            var color = fontSize == 12 ? "0.03 0.42 0.75 rg " : "0.10 0.16 0.23 rg ";
            commands.Append("BT ").Append(font).Append(' ').Append(fontSize).Append(" Tf ")
                .Append(color).Append("50 ").Append(y)
                .Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
            y -= line.Length == 0 ? 12 : fontSize == 16 ? 26 : 20;
        }

        var stream = Encoding.ASCII.GetBytes(commands.ToString());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R /F2 6 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return output.ToArray();
    }

    private static void DrawBrandMark(StringBuilder commands, double x, double y, double scale, string blue, string teal)
    {
        string N(double value) => (value * scale).ToString("0.###", CultureInfo.InvariantCulture);
        string X(double value) => (x + value * scale).ToString("0.###", CultureInfo.InvariantCulture);
        string Y(double value) => (y + value * scale).ToString("0.###", CultureInfo.InvariantCulture);

        commands.Append("q ").Append(blue).Append(" rg ")
            .Append(X(0)).Append(' ').Append(Y(20)).Append(" m ")
            .Append(X(13)).Append(' ').Append(Y(0)).Append(" l ")
            .Append(X(48)).Append(' ').Append(Y(0)).Append(" l ")
            .Append(X(34)).Append(' ').Append(Y(20)).Append(" l ")
            .Append(X(20)).Append(' ').Append(Y(40)).Append(" l ")
            .Append(X(5)).Append(' ').Append(Y(40)).Append(" l h f Q\n");
        commands.Append("q ").Append(teal).Append(" rg ")
            .Append(X(30)).Append(' ').Append(Y(0)).Append(" m ")
            .Append(X(45)).Append(' ').Append(Y(22)).Append(" l ")
            .Append(X(60)).Append(' ').Append(Y(22)).Append(" l ")
            .Append(X(45)).Append(' ').Append(Y(0)).Append(" l h f Q\n");

        // Small white car and scooter cues echo the selected multi-mobility artwork.
        commands.Append("q 1 1 1 rg ")
            .Append(X(9)).Append(' ').Append(Y(23)).Append(" m ")
            .Append(X(14)).Append(' ').Append(Y(15)).Append(" l ")
            .Append(X(26)).Append(' ').Append(Y(15)).Append(" l ")
            .Append(X(31)).Append(' ').Append(Y(23)).Append(" l h f ")
            .Append(X(10)).Append(' ').Append(Y(21)).Append(" 20 5 re f ")
            .Append(X(15)).Append(' ').Append(Y(19)).Append(" 3 3 re f ")
            .Append(X(24)).Append(' ').Append(Y(19)).Append(" 3 3 re f Q\n");
    }

    private static string Escape(string value)
    {
        var ascii = new string(value.Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());
        return ascii.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
