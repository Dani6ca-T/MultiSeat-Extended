/// @file multiseat_input.h
/// @brief Public C API for MultiSeatInputHook.dll
///
/// Provides session-aware keyboard/mouse input filtering for the MultiSeat
/// multi-seat streaming system. When loaded into a target session,
/// this DLL installs low-level hooks that filter input events by
/// Windows Session ID, preventing input bleed between sessions.
///
/// Usage from C# (P/Invoke):
///   [DllImport("MultiSeatInputHook.dll")]
///   static extern int MultiSeatHook_Install(uint targetSessionId);
///
///   [DllImport("MultiSeatInputHook.dll")]
///   static extern void MultiSeatHook_Uninstall();

#pragma once

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Install low-level keyboard and mouse hooks that filter by session ID.
/// Only events originating from the specified session will be passed through;
/// all others are swallowed (the hook returns 1 to block them).
///
/// @param targetSessionId  The Windows Session ID that should receive input.
///                         Pass 0xFFFFFFFF to allow all sessions (no filtering).
/// @return 0 on success, Win32 error code on failure.
int __stdcall MultiSeatHook_Install(DWORD targetSessionId);

/// Remove the installed hooks and clean up.
void __stdcall MultiSeatHook_Uninstall(void);

/// Check if hooks are currently installed.
/// @return TRUE if hooks are active, FALSE otherwise.
BOOL __stdcall MultiSeatHook_IsInstalled(void);

/// Get the session ID that the hooks are filtering for.
/// @return The target session ID, or 0xFFFFFFFF if not filtering.
DWORD __stdcall MultiSeatHook_GetTargetSession(void);

/// Register a RawInput filter for the calling window.
/// This uses RegisterRawInputDevices with RIDEV_INPUTSINK to receive
/// all HID input, then filters by session in the message handler.
///
/// @param hwnd  The window handle to receive WM_INPUT messages.
/// @param targetSessionId  The session to filter for.
/// @return 0 on success, Win32 error code on failure.
int __stdcall MultiSeatRawInput_Register(HWND hwnd, DWORD targetSessionId);

/// Unregister the RawInput filter.
void __stdcall MultiSeatRawInput_Unregister(void);

#ifdef __cplusplus
}
#endif
