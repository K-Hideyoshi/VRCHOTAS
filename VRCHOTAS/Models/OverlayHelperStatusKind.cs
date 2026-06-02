namespace VRCHOTAS.Models;

public enum OverlayHelperStatusKind
{
    HelperStarted,
    HelperConnected,
    Disabled,
    HelperStarting,
    WaitingForSteamVR,
    OpenVrReady,
    D3DReady,
    FallbackRaw,
    OverlayShown,
    OverlayHidden,
    SteamVrQuit,
    LastError,
    HelperStopped
}
