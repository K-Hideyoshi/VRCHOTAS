using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using Valve.VR;

internal sealed class OverlayHandleManager
{
    private const string ToastOverlayKey = "vrchotas.overlay.toast";
    private const string StatusOverlayKey = "vrchotas.overlay.status";
    private const string ToastOverlayName = "VRCHOTAS Toast";
    private const string StatusOverlayName = "VRCHOTAS Status";

    private readonly IAppLogger _logger;
    private ulong _toastHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _statusHandle = OpenVR.k_ulOverlayHandleInvalid;

    public OverlayHandleManager(IAppLogger logger)
    {
        _logger = logger;
    }

    public ulong ToastHandle => _toastHandle;
    public ulong StatusHandle => _statusHandle;

    public bool Ensure(CVROverlay overlay, OverlayVisualKind kind, out ulong handle, VrOverlayPreferences? prefs = null)
    {
        handle = kind == OverlayVisualKind.Toast ? _toastHandle : _statusHandle;
        if (handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            Configure(overlay, handle, kind, prefs); // Reconfigure to apply updated preferences
            return true;
        }

        var key = kind == OverlayVisualKind.Toast ? ToastOverlayKey : StatusOverlayKey;
        var name = kind == OverlayVisualKind.Toast ? ToastOverlayName : StatusOverlayName;
        var error = overlay.FindOverlay(key, ref handle);
        if (error == EVROverlayError.UnknownOverlay)
        {
            error = overlay.CreateOverlay(key, name, ref handle);
        }

        if (error != EVROverlayError.None || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            LogOverlayError($"Create overlay '{key}'", error);
            handle = OpenVR.k_ulOverlayHandleInvalid;
            SetHandle(kind, handle);
            return false;
        }

        if (!Configure(overlay, handle, kind, prefs))
        {
            Destroy(overlay, kind);
            handle = OpenVR.k_ulOverlayHandleInvalid;
            return false;
        }

        // Start invisible – we keep the overlay permanently "shown" in SteamVR so
        // that texture updates are always processed by the compositor.  Visibility
        // is controlled exclusively via alpha (0 = hidden, >0 = visible).
        overlay.SetOverlayAlpha(handle, 0f);
        var showError = overlay.ShowOverlay(handle);
        if (showError != EVROverlayError.None)
        {
            LogOverlayError($"Pre-warm ShowOverlay '{key}'", showError);
            // Non-fatal: we still store the handle and try showing later.
        }

        SetHandle(kind, handle);
        return true;
    }

    public bool Show(CVROverlay overlay, OverlayVisualKind kind, VrOverlayPreferences? prefs)
    {
        if (!Ensure(overlay, kind, out var handle, prefs))
        {
            return false;
        }

        var transform = kind == OverlayVisualKind.Toast ? OverlayPlacement.GetToastTransform(prefs) : OverlayPlacement.GetStatusTransform(prefs);
        var setTransformError = overlay.SetOverlayTransformTrackedDeviceRelative(handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform);
        if (setTransformError != EVROverlayError.None)
        {
            LogOverlayError($"Position {kind} overlay", setTransformError);
            Destroy(overlay, kind);
            return false;
        }

        // Restore visibility by setting alpha back to the intended value.
        float alpha = kind == OverlayVisualKind.Toast
            ? (float)(prefs?.ToastOpacity ?? 1.0)
            : (float)(prefs?.MarkerOpacity ?? 0.8);
        overlay.SetOverlayAlpha(handle, alpha);

        return true;
    }

    public void Hide(CVROverlay? overlay, OverlayVisualKind kind)
    {
        var handle = kind == OverlayVisualKind.Toast ? _toastHandle : _statusHandle;
        if (overlay is null || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        var error = overlay.SetOverlayAlpha(handle, 0f);
        if (error is not EVROverlayError.None and not EVROverlayError.InvalidHandle)
        {
            LogOverlayError($"SetAlpha for Hide {kind} overlay", error);
            Destroy(overlay, kind);
        }
    }

    public void HideAll(CVROverlay? overlay)
    {
        Hide(overlay, OverlayVisualKind.Toast);
        Hide(overlay, OverlayVisualKind.Status);
    }

    public void Destroy(CVROverlay? overlay, OverlayVisualKind kind)
    {
        var handle = kind == OverlayVisualKind.Toast ? _toastHandle : _statusHandle;
        SetHandle(kind, OpenVR.k_ulOverlayHandleInvalid);
        if (overlay is null || handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        var error = overlay.DestroyOverlay(handle);
        if (error is not EVROverlayError.None and not EVROverlayError.InvalidHandle)
        {
            LogOverlayError($"Destroy {kind} overlay", error);
        }
    }

    public void DestroyAll(CVROverlay? overlay)
    {
        Destroy(overlay, OverlayVisualKind.Toast);
        Destroy(overlay, OverlayVisualKind.Status);
    }

    private bool Configure(CVROverlay overlay, ulong handle, OverlayVisualKind kind, VrOverlayPreferences? prefs)
    {
        float width = kind == OverlayVisualKind.Toast
            ? 0.58f
            : (float)(prefs?.MarkerSize ?? 32.0) / 100f;

        return Check(overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.None), $"Set {kind} input method")
            && Check(overlay.SetOverlayTexelAspect(handle, 1f), $"Set {kind} texel aspect")
            && Check(overlay.SetOverlayColor(handle, 1f, 1f, 1f), $"Set {kind} color")
            && Check(overlay.SetOverlayWidthInMeters(handle, width), $"Set {kind} width");
    }

    private bool Check(EVROverlayError error, string operation)
    {
        if (error == EVROverlayError.None)
        {
            return true;
        }

        LogOverlayError(operation, error);
        return false;
    }

    private void SetHandle(OverlayVisualKind kind, ulong handle)
    {
        if (kind == OverlayVisualKind.Toast)
        {
            _toastHandle = handle;
            return;
        }

        _statusHandle = handle;
    }

    private void LogOverlayError(string operation, EVROverlayError error)
    {
        if (error != EVROverlayError.None)
        {
            _logger.Warning(nameof(OverlayHandleManager), $"{operation} failed: {error}");
        }
    }
}
