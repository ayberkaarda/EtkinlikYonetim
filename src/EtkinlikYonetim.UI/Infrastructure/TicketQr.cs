using QRCoder;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// Draws the QR code of a ticket code with QRCoder (MIT licensed, managed). Error correction level M keeps
/// the code small while a screen or a print with some glare still scans. The image holds only the code.
/// </summary>
internal static class TicketQr
{
    private const int PixelsPerModule = 8;

    /// <summary>A new bitmap of the QR code of <paramref name="code"/>; the caller disposes it.</summary>
    public static Bitmap Render(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(PixelsPerModule);

        // Image.FromStream needs its stream for the lifetime of the image; a copy detaches it.
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}