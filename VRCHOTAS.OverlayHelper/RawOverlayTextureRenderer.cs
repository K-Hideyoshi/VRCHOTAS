using System.Runtime.InteropServices;
using Valve.VR;

internal sealed class RawOverlayTextureRenderer : IOverlayTextureRenderer
{
    private readonly Func<string, OverlayVisualKind, OverlayBitmapFrame> _renderBitmap;

    public RawOverlayTextureRenderer(Func<string, OverlayVisualKind, OverlayBitmapFrame> renderBitmap)
    {
        _renderBitmap = renderBitmap;
    }

    public string Name => "RawCompatibility";
    public bool IsReady => true;

    public OverlayTextureUploadResult Upload(CVROverlay overlay, ulong handle, string text, OverlayVisualKind kind)
    {
        var frame = _renderBitmap(text, kind);
        var rgbaPixels = ConvertBgraToRgba(frame.Pixels);
        var gcHandle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
        try
        {
            var error = overlay.SetOverlayRaw(
                handle,
                gcHandle.AddrOfPinnedObject(),
                (uint)frame.Width,
                (uint)frame.Height,
                4);
            return OverlayTextureUploadResult.FromError(error, usedFallback: true);
        }
        finally
        {
            gcHandle.Free();
        }
    }

    public void Dispose()
    {
    }

    private static byte[] ConvertBgraToRgba(byte[] bgraPixels)
    {
        var rgbaPixels = new byte[bgraPixels.Length];
        for (var index = 0; index < bgraPixels.Length; index += 4)
        {
            rgbaPixels[index] = bgraPixels[index + 2];
            rgbaPixels[index + 1] = bgraPixels[index + 1];
            rgbaPixels[index + 2] = bgraPixels[index];
            rgbaPixels[index + 3] = bgraPixels[index + 3];
        }

        return rgbaPixels;
    }
}
