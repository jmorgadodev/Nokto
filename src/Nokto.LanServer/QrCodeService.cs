using QRCoder;

namespace Nokto.LanServer;

public static class QrCodeService
{
    public static byte[] GeneratePngBytes(string url, int pixelsPerModule = 10)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule);
    }
}
