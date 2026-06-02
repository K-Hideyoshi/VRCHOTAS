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
        var gcHandle = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
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
}
