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
    private readonly Dictionary<OverlayVisualKind, Texture2D> _textures = new();

    public D3D11OverlayTextureRenderer(Func<string, OverlayVisualKind, OverlayBitmapFrame> renderBitmap)
    {
        _renderBitmap = renderBitmap;
        _device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _device.ImmediateContext;
    }

    public string Name => "D3D11";
    public bool IsReady => !_device.IsDisposed;

    public OverlayTextureUploadResult Upload(CVROverlay overlay, ulong handle, string text, OverlayVisualKind kind)
    {
        var frame = _renderBitmap(text, kind);
        var texture = EnsureTexture(kind, frame.Width, frame.Height);
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

    private Texture2D EnsureTexture(OverlayVisualKind kind, int width, int height)
    {
        if (_textures.TryGetValue(kind, out var existing)
            && existing.Description.Width == width
            && existing.Description.Height == height)
        {
            return existing;
        }

        existing?.Dispose();
        var texture = new Texture2D(_device, new Texture2DDescription
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
        _textures[kind] = texture;
        return texture;
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
        }
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _context.Dispose();
        _device.Dispose();
    }
}
