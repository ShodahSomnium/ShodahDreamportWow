# How Somnium detects VR (headset) and desktop (non-XR)

*Checked against the Somnium client source (`E:\Somnium Space\Main Build`) and the WoW Dreamport code, 2026-11-16.*

## Summary

- **Somnium does not detect a headset.** The **launcher** decides: when you press Play you choose VR or desktop, and the game is started in that mode.
- In **VR mode** the client starts Unity's XR system. In **desktop mode** it shuts XR down.
- **A world (Dreamport)** can't ask Somnium "is this player in VR?" because the world SDK (the Bridge) has no such flag. Instead it checks whether Unity's XR is actually running.

---

## 1. The launcher picks the mode

File: `Assets/_SomniumSpace/Arguments/Scripts/ArgumentsContainer.cs`, property `LaunchMode`

| How the game was started | Mode used |
|---|---|
| Release build, from the launcher | the launcher's choice: `MODE_XR` or `MODE_NON_XR` |
| Release build, **without** the launcher | always `MODE_NON_XR` (desktop) |
| Editor / debug build | the dev config's mode (can override the launcher's) |

`LaunchModeType` has two values: `MODE_XR` and `MODE_NON_XR`.

## 2. The client starts or stops XR

File: `Assets/_SomniumSpace/XR/Scripts/XRSystem.cs`

`XRSystem.Initialize()` calls `ApplyLaunchMode(ArgumentsContainer.LaunchMode)`, which does the following.

**`MODE_XR` → `StartXR()`:**
1. Reads the Windows registry to see whether **SteamVR** is the default XR runtime (`IsSteamVRAsDefaultXRRuntime`).
2. If SteamVR is **not** the default, removes the OpenVR loader, so the other runtime is used (for example Meta / Oculus OpenXR).
3. `XRGeneralSettings.Instance.Manager.InitializeLoader()`
   - **Fails** (no active loader): logs `[XRSystem]: Initializing XR Failed` and the player gets no VR.
   - **Works:** `StartSubsystems()`, then `OpenVRStarter.Initialize()` (OpenVR for **full-body trackers**, connected only after the OpenXR session owns SteamVR).

**`MODE_NON_XR` → `StopXR()`:**
- Stops the XR subsystems, de-initialises the loader and shuts OpenVR down.

**Consequences**
- A headset plugged in with **desktop** chosen stays **desktop**.
- **VR** chosen with **no headset** means XR fails to start (see the log line above).

## 3. How a world detects it (the WoW Dreamport)

There's no Bridge member for VR / desktop (the Bridge code was searched), so the Dreamport asks Unity directly:

```csharp
using UnityEngine.XR;

// true = the player is in a headset (VR), false = desktop
public static bool XrPresent
{
    get
    {
        try { return XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName); }
        catch (System.Exception) { return false; }
    }
}
```

- File: `Assets/#User/SomniumWoW/Game/Vr/WowPlayer.cs` (`WowPlayer.XrPresent`). It's cached once per frame and re-checked every frame, so it always matches what the client actually started.
- **VR = XR is running AND a VR device is loaded.** Anything else counts as desktop.
- Everything VR-specific in the Dreamport keys off this flag: wrist and Gear Select spell menus, the strike ring, VR swimming controls, VR windows, the immersive bow, and so on.
- It's also sent to other players: bit 16 of record int 4 means VR (`WowNetWire`). Viewers use it to decide, for example, to leave a VR player's tracked hands alone when posing their avatar.

## 4. Quick checks

| Want to know | Look at |
|---|---|
| Which mode the client started in | Player.log: `[XRSystem]: Starting XR...` (VR) or `[XRSystem]: Stopping XR...` (desktop) |
| VR failed to start | Player.log: `[XRSystem]: Initializing XR Failed` |
| Whether SteamVR is the default runtime | Windows registry: the default OpenXR runtime (`IsSteamVRAsDefaultXRRuntime` in `XRSystem.cs`) |
| What a world sees | `XRSettings.isDeviceActive` + `XRSettings.loadedDeviceName` |
