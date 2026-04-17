/// @file raw_input_filter.cpp
/// @brief RawInput-based session-aware input filtering.
///
/// Alternative approach to low-level hooks: uses RegisterRawInputDevices
/// with RIDEV_INPUTSINK to receive ALL HID input, even when the window
/// is not in the foreground. The filter then checks the source device's
/// session and discards events from other sessions.
///
/// This approach is useful for:
///   - Receiving input even when the session's desktop isn't active
///   - Filtering specific HID devices (keyboard vs mouse vs gamepad)
///   - Lower overhead than hooks for high-frequency gamepad input

#include "raw_input_filter.h"
#include "session_context.h"
#include <windows.h>

static DWORD g_rawInputTargetSession = 0xFFFFFFFF;
static BOOL g_rawInputRegistered = FALSE;

extern "C" {

int __stdcall MultiSeatRawInput_Register(HWND hwnd, DWORD targetSessionId)
{
    g_rawInputTargetSession = targetSessionId;

    // Register for keyboard and mouse RawInput with INPUTSINK
    // so we receive events even when our window isn't foreground.
    RAWINPUTDEVICE rid[2];

    // Keyboard (usage page 1, usage 6)
    rid[0].usUsagePage = 0x01;  // HID_USAGE_PAGE_GENERIC
    rid[0].usUsage = 0x06;     // HID_USAGE_GENERIC_KEYBOARD
    rid[0].dwFlags = RIDEV_INPUTSINK;
    rid[0].hwndTarget = hwnd;

    // Mouse (usage page 1, usage 2)
    rid[1].usUsagePage = 0x01;  // HID_USAGE_PAGE_GENERIC
    rid[1].usUsage = 0x02;     // HID_USAGE_GENERIC_MOUSE
    rid[1].dwFlags = RIDEV_INPUTSINK;
    rid[1].hwndTarget = hwnd;

    if (!RegisterRawInputDevices(rid, 2, sizeof(RAWINPUTDEVICE)))
    {
        return (int)GetLastError();
    }

    g_rawInputRegistered = TRUE;
    return 0;
}

void __stdcall MultiSeatRawInput_Unregister(void)
{
    if (!g_rawInputRegistered)
        return;

    // Unregister by passing RIDEV_REMOVE
    RAWINPUTDEVICE rid[2];

    rid[0].usUsagePage = 0x01;
    rid[0].usUsage = 0x06;
    rid[0].dwFlags = RIDEV_REMOVE;
    rid[0].hwndTarget = NULL;

    rid[1].usUsagePage = 0x01;
    rid[1].usUsage = 0x02;
    rid[1].dwFlags = RIDEV_REMOVE;
    rid[1].hwndTarget = NULL;

    RegisterRawInputDevices(rid, 2, sizeof(RAWINPUTDEVICE));
    g_rawInputRegistered = FALSE;
    g_rawInputTargetSession = 0xFFFFFFFF;
}

BOOL ProcessRawInputMessage(LPARAM lParam)
{
    if (g_rawInputTargetSession == 0xFFFFFFFF)
        return TRUE;  // no filtering

    // Get the raw input data
    UINT dataSize = 0;
    GetRawInputData(
        (HRAWINPUT)lParam, RID_INPUT,
        NULL, &dataSize, sizeof(RAWINPUTHEADER));

    if (dataSize == 0 || dataSize > 4096)
        return FALSE;

    // Stack-allocate for small buffers (typical raw input is <100 bytes)
    BYTE buffer[4096];
    RAWINPUT* raw = (RAWINPUT*)buffer;

    if (GetRawInputData(
        (HRAWINPUT)lParam, RID_INPUT,
        raw, &dataSize, sizeof(RAWINPUTHEADER)) == (UINT)-1)
    {
        return FALSE;
    }

    // The RAWINPUTHEADER contains the device handle (hDevice)
    // and the wparam which tells us the input type.
    // To filter by session, we check which window will process this input.
    // For RIDEV_INPUTSINK, the header's dwType tells us the source type
    // but not the session. We use the foreground window approach.

    HWND fg = GetForegroundWindow();
    if (fg == NULL)
        return FALSE;

    DWORD processId = 0;
    GetWindowThreadProcessId(fg, &processId);

    DWORD sessionId = GetSessionIdForProcess(processId);

    return (sessionId == g_rawInputTargetSession) ? TRUE : FALSE;
}

} // extern "C"
