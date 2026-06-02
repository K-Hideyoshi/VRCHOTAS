using Valve.VR;

internal interface IOverlayTextureRenderer : IDisposable
{
    string Name { get; }
    bool IsReady { get; }
    OverlayTextureUploadResult Upload(CVROverlay overlay, ulong handle, string text, OverlayVisualKind kind);
}
