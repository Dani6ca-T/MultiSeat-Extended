#pragma once
#include <windows.h>

/// Low-level keyboard hook procedure.
/// Filters keyboard events by checking the foreground window's process session ID.
/// If the session doesn't match the target, the event is swallowed.
LRESULT CALLBACK LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam);

/// Low-level mouse hook procedure.
/// Same filtering logic as the keyboard hook.
LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam);
