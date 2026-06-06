using Valve.VR;
using VRCHOTAS.Models;

internal static class OverlayPlacement
{
    private const float StatusLeft = -0.28f;
    private const float StatusRight = 0.28f;
    private const float StatusBottom = -0.27f;
    private const float StatusTop = 0.27f;

    public static HmdMatrix34_t GetToastTransform(VrOverlayPreferences? prefs)
    {
        var ratio = Math.Clamp((float)(prefs?.ToastPositionY ?? 0.32), 0f, 1f);
        var scale = Math.Clamp((float)(prefs?.OverlaySizeScale ?? 1.2), 0.5f, 3f);
        var y = scale * ((2f * ratio) - 1f);
        var z = -(float)(prefs?.OverlayDistanceMeters ?? 0.8);
        return CreateTransform(0f, y, z);
    }

    public static HmdMatrix34_t GetStatusTransform(VrOverlayPreferences? prefs)
    {
        var ratioX = Math.Clamp((float)(prefs?.MarkerPositionX ?? 0.0), 0f, 1f);
        var ratioY = Math.Clamp((float)(prefs?.MarkerPositionY ?? 0.0), 0f, 1f);
        var scale = Math.Clamp((float)(prefs?.OverlaySizeScale ?? 1.0), 0.5f, 3f);
        var x = (StatusLeft + ((StatusRight - StatusLeft) * ratioX)) * scale;
        var y = (StatusBottom + ((StatusTop - StatusBottom) * ratioY)) * scale;
        var z = -(float)(prefs?.OverlayDistanceMeters ?? 0.8);
        return CreateTransform(x, y, z);
    }

    private static HmdMatrix34_t CreateTransform(float x, float y, float z)
    {
        return new HmdMatrix34_t
        {
            m0 = 1f,
            m1 = 0f,
            m2 = 0f,
            m3 = x,
            m4 = 0f,
            m5 = 1f,
            m6 = 0f,
            m7 = y,
            m8 = 0f,
            m9 = 0f,
            m10 = 1f,
            m11 = z
        };
    }
}
