#pragma once
#include <windows.h>

/// Get the Windows Session ID for the calling process.
DWORD GetCurrentSessionId();

/// Get the Windows Session ID for a given process ID.
/// Returns 0xFFFFFFFF on failure.
DWORD GetSessionIdForProcess(DWORD processId);
