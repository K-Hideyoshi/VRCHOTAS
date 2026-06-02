using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

internal sealed class OverlayBitmapFactory
{
    public const int ToastWidth = 1024;
    public const int ToastHeight = 256;
    public const int StatusWidth = 512;
    public const int StatusHeight = 160;

    public OverlayBitmapFrame Render(string text, OverlayVisualKind kind)
    {
        var width = kind == OverlayVisualKind.Toast ? ToastWidth : StatusWidth;
        var height = kind == OverlayVisualKind.Toast ? ToastHeight : StatusHeight;
        var bitmap = RenderBitmap(text, kind, width, height);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new OverlayBitmapFrame(bitmap, pixels, stride);
    }

    private static RenderTargetBitmap RenderBitmap(string text, OverlayVisualKind kind, int width, int height)
    {
        var fontFamily = new WpfFontFamily("Segoe UI");
        var typeface = new Typeface(
            fontFamily,
            System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.SemiBold,
            System.Windows.FontStretches.Normal);
        var fontSize = kind == OverlayVisualKind.Toast ? 52d : 40d;
        var horizontalPadding = kind == OverlayVisualKind.Toast ? 56d : 36d;
        var maxTextWidth = Math.Max(1d, width - (horizontalPadding * 2d));
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            WpfFlowDirection.LeftToRight,
            typeface,
            fontSize,
            System.Windows.Media.Brushes.White,
            1d)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = System.Windows.TextTrimming.CharacterEllipsis,
            TextAlignment = kind == OverlayVisualKind.Toast ? System.Windows.TextAlignment.Center : System.Windows.TextAlignment.Left
        };

        var background = kind == OverlayVisualKind.Toast
            ? new SolidColorBrush(WpfColor.FromArgb(220, 20, 20, 24))
            : new SolidColorBrush(WpfColor.FromArgb(220, 0, 80, 0));
        var border = kind == OverlayVisualKind.Toast
            ? new WpfPen(new SolidColorBrush(WpfColor.FromArgb(200, 120, 180, 255)), 4)
            : new WpfPen(new SolidColorBrush(WpfColor.FromArgb(230, 190, 255, 190)), 4);
        background.Freeze();
        border.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(background, border, new System.Windows.Rect(0, 0, width, height), 28, 28);
            var x = kind == OverlayVisualKind.Toast
                ? Math.Max(horizontalPadding, (width - formattedText.WidthIncludingTrailingWhitespace) / 2d)
                : horizontalPadding;
            var y = Math.Max(0d, (height - formattedText.Height) / 2d);
            context.DrawText(formattedText, new WpfPoint(x, y));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
