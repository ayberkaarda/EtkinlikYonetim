using System.Drawing.Drawing2D;
using EtkinlikYonetim.UI.Theming;
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

/// <summary>
/// Shows a QR code scaled to its box with hard module edges (no smoothing, which would blur them for a
/// scanner), always dark on white whatever the theme, as scanners expect.
/// </summary>
internal sealed class QrView : PictureBox
{
    public QrView()
    {
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.White;
        Size = new Size(Sizes.QrCode, Sizes.QrCode);
        Margin = Padding.Empty;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Biletin QR kodu";
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        ArgumentNullException.ThrowIfNull(pe);
        pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        base.OnPaint(pe);
    }
}
