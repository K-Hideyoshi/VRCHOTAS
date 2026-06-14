# VRCHOTAS Release Notes

## v1.3.0 (2026-06-14)

**VR Overlay**
- Introduced an OpenVR overlay system that renders information inside the VR headset.
- A separate overlay helper process (`VRCHOTAS.OverlayHelper.exe`) handles overlay initialization and rendering via named-pipe IPC.
- Overlay placement, visual style, and preferences are configurable through the desktop UI.

**Keyboard Mapping**
- Added **Keyboard** as a new mapping target in the Mapping Editor.
- Map HOTAS/joystick inputs to keyboard key presses — send keys globally or target a specific window by title or process name.
- Supports modifier keys (Ctrl, Shift, Alt).

**Anchor Point Management**
- Per-configuration hand anchor points are now saved and restored automatically.
- Debounced persistence prevents excessive disk writes during active use.
- Anchors are cleaned up when switching or deleting configurations.

**Mapping Editor Improvements**
- Added trigger configuration options (toggle mode support for keyboard and controller pose actions).
- State panel visibility is now configurable for a cleaner editing experience.

**General Improvements**
- Code refactoring for better maintainability.
- Key bindings are now suppressed when toggling the master switch off → on, preventing unintended input bursts.
