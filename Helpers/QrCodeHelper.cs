using System.Drawing;
using System.Drawing.Drawing2D;
using Net.Codecrete.QrCodeGenerator;

namespace HermesHub.Helpers;

public static class QrCodeHelper
{
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

    public static string GenerateQrCodeBase64(string url, int modulePixelSize = 5, int quietZone = 2)
    {
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
    }
}
