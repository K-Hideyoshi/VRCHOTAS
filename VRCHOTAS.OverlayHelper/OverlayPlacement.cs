using Valve.VR;

internal static class OverlayPlacement
{
    public static HmdMatrix34_t ToastTransform => CreateTransform(0f, -0.34f, -0.86f);
    public static HmdMatrix34_t StatusTransform => CreateTransform(-0.28f, -0.27f, -0.8f);

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
