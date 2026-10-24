#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
#endif
using Net.Codecrete.QrCodeGenerator;

namespace AgentsHub.Helpers;

public static class QrCodeHelper
{
#if WINDOWS
    public static Bitmap GenerateQrCode(string url, int modulePixelSize = 4, int quietZone = 2)
    {
        var qr = QrCode.EncodeText(url, QrCode.Ecc.Medium);
        var size = (qr.Size + quietZone * 2) * modulePixelSize;
        var bitmap = new Bitmap(size, size);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.White);

            using var brush = new SolidBrush(Color.FromArgb(9, 9, 11));
            for (var y = 0; y < qr.Size; y++)
            {
                for (var x = 0; x < qr.Size; x++)
                {
                    if (qr.GetModule(x, y))
                    {
                        g.FillRectangle(
                            brush,
                            (x + quietZone) * modulePixelSize,
                            (y + quietZone) * modulePixelSize,
                            modulePixelSize,
                            modulePixelSize
                        );
                    }
                }
            }
        }

        return bitmap;
    }
#endif

    public static string GenerateQrCodeBase64(string url, int modulePixelSize = 5, int quietZone = 2)
    {
#if WINDOWS
        try
        {
            using var bitmap = GenerateQrCode(url, modulePixelSize, quietZone);
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
#else
        try
        {
            var qr = QrCode.EncodeText(url, QrCode.Ecc.Medium);
            var svg = qr.ToSvgString(quietZone);
            return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        }
        catch
        {
            return string.Empty;
        }
#endif
    }
}
