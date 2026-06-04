using Valve.VR;
using VRCHOTAS.Models;

internal static class OverlayPlacement
{
    public static HmdMatrix34_t GetToastTransform(VrOverlayPreferences? prefs) => CreateTransform(0f, -0.34f, -0.86f);
    public static HmdMatrix34_t GetStatusTransform(VrOverlayPreferences? prefs) => CreateTransform(-0.28f + (float)(prefs?.MarkerPositionX ?? 0.0), -0.27f + (float)(prefs?.MarkerPositionY ?? 0.0), -0.8f);

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
