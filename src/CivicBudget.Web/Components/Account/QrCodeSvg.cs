using Microsoft.AspNetCore.Components;
using QRCoder;

namespace CivicBudget.Web.Components.Account;

/// <summary>
/// The QR code an authenticator app scans, drawn on the server as SVG. No script is sent to the
/// sign-in pages, and the secret never leaves the page it is shown on.
/// </summary>
public static class QrCodeSvg
{
    public static MarkupString For(string text)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return new MarkupString(svg.GetGraphic(new System.Drawing.Size(200, 200), "#0F2A44", "#FFFFFF", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute));
    }
}
