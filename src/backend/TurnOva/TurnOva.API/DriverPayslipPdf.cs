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
            "TURNOVA",
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
        var y = 790;
        foreach (var line in lines)
        {
            var fontSize = line switch
            {
                "TURNOVA" => 22,
                "MONTHLY DRIVER PAYSLIP" => 16,
                "DRIVER AND EMPLOYER" or "MONTHLY WORK SUMMARY" or "PAYMENT INFORMATION" => 12,
                _ => 10
            };
            commands.Append("BT /F1 ").Append(fontSize).Append(" Tf 50 ").Append(y)
                .Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
            y -= line.Length == 0 ? 12 : fontSize == 22 ? 32 : fontSize == 16 ? 26 : 20;
        }

        var stream = Encoding.ASCII.GetBytes(commands.ToString());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}endstream"
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