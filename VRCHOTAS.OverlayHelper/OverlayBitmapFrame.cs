using System.Windows.Media.Imaging;

internal sealed class OverlayBitmapFrame
{
    public OverlayBitmapFrame(BitmapSource bitmap, byte[] pixels, int stride)
    {
        Bitmap = bitmap;
        Pixels = pixels;
        Stride = stride;
    }

    public BitmapSource Bitmap { get; }
    public byte[] Pixels { get; }
    public int Stride { get; }
    public int Width => Bitmap.PixelWidth;
    public int Height => Bitmap.PixelHeight;
}
