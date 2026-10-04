using Microsoft.AspNetCore.Html;
using QRCoder;

namespace Web.Labels;

/// <summary>
/// QR codes as inline SVG, sharp at any size (CSS sets the size). A parcel label's holds the tracking code only; the
/// scanner looks everything else up in its own courier.
/// </summary>
public static class LabelQrCode
{
    public static IHtmlContent Svg(string content)
    {
        // Medium error correction survives a scuffed or taped-over corner
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);

        return new HtmlString(new SvgQRCode(data).GetGraphic(
            1,
            "#000000",
            "#ffffff",
            drawQuietZones: true,
            SvgQRCode.SizingMode.ViewBoxAttribute));
    }
}
