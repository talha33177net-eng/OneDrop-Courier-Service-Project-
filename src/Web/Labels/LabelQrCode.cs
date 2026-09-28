using Microsoft.AspNetCore.Html;
using Domain.Orders;
using QRCoder;

namespace Web.Labels;

/// <summary>
/// The QR code printed on a parcel label, as inline SVG so it prints sharp at any size (CSS sets the size). It holds
/// the label code only; the scanner looks everything else up in its own tenant.
/// </summary>
public static class LabelQrCode
{
    public static IHtmlContent Svg(PackageLabel label)
    {
        // Medium error correction survives a scuffed or taped-over corner
        using var data = QRCodeGenerator.GenerateQrCode(label.ToString(), QRCodeGenerator.ECCLevel.M);

        return new HtmlString(new SvgQRCode(data).GetGraphic(
            1,
            "#000000",
            "#ffffff",
            drawQuietZones: true,
            SvgQRCode.SizingMode.ViewBoxAttribute));
    }
}
