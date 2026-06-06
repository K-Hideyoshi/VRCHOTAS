using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Valve.VR;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

internal sealed class D3D11OverlayTextureRenderer : IOverlayTextureRenderer
{
    private readonly Func<string, OverlayVisualKind, OverlayBitmapFrame> _renderBitmap;
    private readonly Device _device;
    private readonly DeviceContext _context;

    // Two textures per visual kind: ping-pong double buffering.
    //
    // SteamVR's compositor caches its internal SRV by NativePointer (COM object identity).
    // If we pass the same pointer on two consecutive SetOverlayTexture calls, the compositor
    // assumes the texture hasn't changed and displays the cached (stale) frame.
    //
    // By alternating between two pre-allocated textures on every Upload, the pointer
    // always differs from the previous call, forcing SteamVR to re-read the GPU data.
    private readonly Texture2D[] _toastTextures;
    private readonly Texture2D[] _statusTextures;
    private int _toastIndex;
    private int _statusIndex;

    public D3D11OverlayTextureRenderer(Func<string, OverlayVisualKind, OverlayBitmapFrame> renderBitmap)
    {
        _renderBitmap = renderBitmap;
        _device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _device.ImmediateContext;

        _toastTextures = new[]
        {
            AllocateTexture(OverlayBitmapFactory.ToastWidth,  OverlayBitmapFactory.ToastHeight),
            AllocateTexture(OverlayBitmapFactory.ToastWidth,  OverlayBitmapFactory.ToastHeight),
        };
        _statusTextures = new[]
        {
            AllocateTexture(OverlayBitmapFactory.StatusWidth, OverlayBitmapFactory.StatusHeight),
            AllocateTexture(OverlayBitmapFactory.StatusWidth, OverlayBitmapFactory.StatusHeight),
        };
    }

    public string Name => "D3D11";
    public bool IsReady => !_device.IsDisposed;

    public OverlayTextureUploadResult Upload(CVROverlay overlay, ulong handle, string text, OverlayVisualKind kind)
    {
        // Toggle to the back buffer so SteamVR always receives a different NativePointer.
        Texture2D texture;
        if (kind == OverlayVisualKind.Toast)
        {
            _toastIndex = 1 - _toastIndex;
            texture = _toastTextures[_toastIndex];
        }
        else
        {
            _statusIndex = 1 - _statusIndex;
            texture = _statusTextures[_statusIndex];
        }

        var frame = _renderBitmap(text, kind);
        WritePixels(texture, frame);

        var openVrTexture = new Texture_t
        {
            handle = texture.NativePointer,
            eType = ETextureType.DirectX,
            eColorSpace = EColorSpace.Auto
        };
        var error = overlay.SetOverlayTexture(handle, ref openVrTexture);
        return OverlayTextureUploadResult.FromError(error);
    }

    private Texture2D AllocateTexture(int width, int height)
    {
        return new Texture2D(_device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.Write,
            OptionFlags = ResourceOptionFlags.None
        });
    }

    private void WritePixels(Texture2D texture, OverlayBitmapFrame frame)
    {
        var dataBox = _context.MapSubresource(texture, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            for (var row = 0; row < frame.Height; row++)
            {
                var sourceOffset = row * frame.Stride;
                var destination = dataBox.DataPointer + (row * dataBox.RowPitch);
                Marshal.Copy(frame.Pixels, sourceOffset, destination, frame.Stride);
            }
        }
        finally
        {
            _context.UnmapSubresource(texture, 0);
            _context.Flush();
        }
    }

    public void Dispose()
    {
        foreach (var t in _toastTextures)  t.Dispose();
        foreach (var t in _statusTextures) t.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
