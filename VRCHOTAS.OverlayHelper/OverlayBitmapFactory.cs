using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using System.Windows;
using System.IO;
using VRCHOTAS.Models;

internal sealed class OverlayBitmapFactory
{
    public const int ToastWidth = 1024;
    public const int ToastHeight = 256;
    public const int StatusWidth = 512;
    public const int StatusHeight = 512; // increased for squared icons

    public OverlayBitmapFrame Render(string text, OverlayVisualKind kind, VrOverlayPreferences? prefs)
    {
        var width = kind == OverlayVisualKind.Toast ? ToastWidth : StatusWidth;
        var height = kind == OverlayVisualKind.Toast ? ToastHeight : StatusHeight;
        var bitmap = RenderBitmap(text, kind, width, height, prefs);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new OverlayBitmapFrame(bitmap, pixels, stride);
    }

    private static RenderTargetBitmap RenderBitmap(string text, OverlayVisualKind kind, int width, int height, VrOverlayPreferences? prefs)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (kind == OverlayVisualKind.Toast)
            {
                RenderToast(context, text, width, height, prefs);
            }
            else
            {
                RenderStatus(context, width, height, prefs);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void RenderToast(DrawingContext context, string text, int width, int height, VrOverlayPreferences? prefs)
    {
        var fontFamily = new WpfFontFamily("Segoe UI");
        var typeface = new Typeface(
            fontFamily,
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
            
        double fontSize = prefs?.ToastTextSize ?? 24.0;
        
        WpfColor themeColor = WpfColor.FromArgb(255, 20, 20, 24);
        try
        {
            if (!string.IsNullOrEmpty(prefs?.ToastBackgroundColor))
            {
                themeColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(prefs.ToastBackgroundColor);
            }
        }
        catch { }

        var toastOpacity = Math.Clamp(prefs?.ToastOpacity ?? 0.8, 0.0, 1.0);
        WpfColor bgColor = WpfColor.FromArgb((byte)(themeColor.A * toastOpacity), themeColor.R, themeColor.G, themeColor.B);
        WpfColor borderColor = WpfColor.FromArgb(255, themeColor.R, themeColor.G, themeColor.B);

        double luminance = (0.2126 * themeColor.R + 0.7152 * themeColor.G + 0.0722 * themeColor.B) / 255.0;
        var textColorBrush = luminance > 0.5 ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;

        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            WpfFlowDirection.LeftToRight,
            typeface,
            fontSize,
            textColorBrush,
            1d);

        double horizontalPadding = 56d;
        double verticalPadding = 24d;
        double paddingSides = horizontalPadding * 2;

        double textWidth = Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace) + 8d;
        double textHeight = Math.Ceiling(formattedText.Height);

        double boxWidth = Math.Min(textWidth + paddingSides, width);
        double boxHeight = Math.Min(textHeight + verticalPadding * 2, height);

        formattedText.MaxLineCount = 1;
        formattedText.MaxTextWidth = Math.Max(10d, boxWidth - paddingSides + 4d);
        formattedText.MaxTextHeight = Math.Max(10d, boxHeight - verticalPadding * 2);
        formattedText.Trimming = TextTrimming.CharacterEllipsis;
        formattedText.TextAlignment = TextAlignment.Center;

        var background = new SolidColorBrush(bgColor);
        var border = new WpfPen(new SolidColorBrush(borderColor), 4);
        background.Freeze();
        border.Freeze();

        double boxX = (width - boxWidth) / 2d;
        double boxY = (height - boxHeight) / 2d;
        context.DrawRoundedRectangle(background, border, new Rect(boxX, boxY, boxWidth, boxHeight), 28, 28);

        double textX = boxX + horizontalPadding;
        double textY = boxY + (boxHeight - formattedText.Height) / 2d;
        context.DrawText(formattedText, new WpfPoint(textX, textY));
    }

    private static void RenderStatus(DrawingContext context, int width, int height, VrOverlayPreferences? prefs)
    {
        string? imgPath = prefs?.MarkerImagePath;
        if (string.IsNullOrEmpty(imgPath))
        {
            imgPath = "icons\\joystick.png";
        }

        try
        {
            var absolutePath = System.IO.Path.IsPathRooted(imgPath) ? imgPath : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imgPath);
            if (File.Exists(absolutePath))
            {
                var bi = new BitmapImage(new Uri(absolutePath, UriKind.Absolute));

                double imgWidth = bi.PixelWidth;
                double imgHeight = bi.PixelHeight;

                if (imgWidth > 0 && imgHeight > 0)
                {
                    double scaleX = (double)width / imgWidth;
                    double scaleY = (double)height / imgHeight;
                    double scale = Math.Min(scaleX, scaleY);

                    double finalWidth = imgWidth * scale;
                    double finalHeight = imgHeight * scale;

                    double x = (width - finalWidth) / 2d;
                    double y = (height - finalHeight) / 2d;

                    context.DrawImage(bi, new Rect(x, y, finalWidth, finalHeight));
                }
            }
        }
        catch
        {
        }
    }
}
