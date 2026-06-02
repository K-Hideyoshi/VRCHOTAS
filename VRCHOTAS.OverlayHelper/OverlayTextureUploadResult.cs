using Valve.VR;

internal readonly record struct OverlayTextureUploadResult(bool Success, EVROverlayError Error, bool UsedFallback)
{
    public static OverlayTextureUploadResult FromError(EVROverlayError error, bool usedFallback = false)
    {
        return new OverlayTextureUploadResult(error == EVROverlayError.None, error, usedFallback);
    }
}
