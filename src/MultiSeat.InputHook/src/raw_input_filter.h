#pragma once
#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Register for RawInput with RIDEV_INPUTSINK to receive all HID events,
/// then filter by session ID when processing WM_INPUT messages.
int __stdcall MultiSeatRawInput_Register(HWND hwnd, DWORD targetSessionId);

/// Unregister the RawInput filter.
void __stdcall MultiSeatRawInput_Unregister(void);

/// Process a WM_INPUT message. Returns TRUE if the input belongs to the
/// target session and should be processed, FALSE if it should be discarded.
BOOL ProcessRawInputMessage(LPARAM lParam);

#ifdef __cplusplus
}
#endif
