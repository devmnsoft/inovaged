using InovaGed.Application.PhysicalArchive;
using QRCoder;

namespace InovaGed.Web.Services;

public sealed class LabelQrCodeService : ILabelQrCodeService
{
    public string CreateTrackingSvg(string authorizedTrackingUrl)
    {
        if (!Uri.TryCreate(authorizedTrackingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("A URL de rastreio autorizada é inválida.", nameof(authorizedTrackingUrl));

        using var data = QRCodeGenerator.GenerateQrCode(uri.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        return new SvgQRCode(data).GetGraphic(4);
    }
}
