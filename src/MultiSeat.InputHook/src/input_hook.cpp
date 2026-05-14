/// @file input_hook.cpp
/// @brief Session-aware low-level keyboard and mouse hooks.
///
/// Installs WH_KEYBOARD_LL and WH_MOUSE_LL hooks that filter input
/// events by Windows Session ID. Events that don't belong to the
/// target session are consumed (blocked from reaching the application).
///
/// This is the critical mechanism for keyboard/mouse isolation between
/// concurrent Windows sessions in the MultiSeat multi-seat system.
///
/// How it works:
///   1. A low-level hook is installed via SetWindowsHookEx.
///   2. On each input event, we check who will receive it by examining
///      the foreground window's owning process.
///   3. We call ProcessIdToSessionId to determine the session.
///   4. If the session matches our target, we pass the event through
///      (CallNextHookEx). If not, we block it (return 1).
///
/// Important notes:
///   - Low-level hooks run in the context of the thread that installed
///     them, NOT the target thread. This means the hook DLL is loaded
///     in the installing process, not injected.
///   - The hook must pump messages (GetMessage loop) to receive
///     hook callbacks. The installing process must have a message loop.
///   - Hook procedures have a ~300ms timeout on Windows — if the proc
///     takes too long, Windows unhooks it. Keep processing minimal.

#include "input_hook.h"
#include "session_context.h"
#include <windows.h>

// ── Global state (shared within the DLL) ────────────────────────────
static HHOOK g_keyboardHook = NULL;
static HHOOK g_mouseHook = NULL;
static DWORD g_targetSessionId = 0xFFFFFFFF;  // 0xFFFFFFFF = no filtering
static HINSTANCE g_hModule = NULL;

// Cache the foreground window's session to avoid repeated lookups
// (the foreground window doesn't change between input events in a single batch)
static HWND g_cachedFgWindow = NULL;
static DWORD g_cachedFgSession = 0xFFFFFFFF;

/// Determine the session ID of the process owning the foreground window.
static DWORD GetForegroundSessionId()
{
    HWND fg = GetForegroundWindow();
    if (fg == NULL)
        return 0xFFFFFFFF;

    // Use cached result if foreground window hasn't changed
    if (fg == g_cachedFgWindow)
        return g_cachedFgSession;

    DWORD processId = 0;
    GetWindowThreadProcessId(fg, &processId);

    DWORD sessionId = GetSessionIdForProcess(processId);

    g_cachedFgWindow = fg;
    g_cachedFgSession = sessionId;

    return sessionId;
}

/// Should this input event be allowed through?
static inline BOOL ShouldPassThrough()
{
    // No filtering mode
    if (g_targetSessionId == 0xFFFFFFFF)
        return TRUE;

    DWORD fgSession = GetForegroundSessionId();

    // If no foreground window exists (lock screen, UAC prompt, window transition,
    // etc.), GetForegroundWindow returns NULL and we get 0xFFFFFFFF back.
    // Blocking input in that case locks the keyboard entirely until a window
    // gets focus. Pass through instead so the user can always type.
    if (fgSession == 0xFFFFFFFF)
        return TRUE;

    return fgSession == g_targetSessionId;
}

LRESULT CALLBACK LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0)
    {
        // Injected events (SendInput/keybd_event) are session-scoped — a process
        // can only inject into its own session, so there is no cross-session bleed
        // to filter. Passing them through immediately avoids blocking Apollo's
        // input injection thread on every keystroke.
        const KBDLLHOOKSTRUCT* khs = reinterpret_cast<const KBDLLHOOKSTRUCT*>(lParam);
        if (khs->flags & LLKHF_INJECTED)
            return CallNextHookEx(g_keyboardHook, nCode, wParam, lParam);

        if (!ShouldPassThrough())
            return 1;
    }

    return CallNextHookEx(g_keyboardHook, nCode, wParam, lParam);
}

LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode >= 0)
    {
        // Same fast-path for injected mouse events — skips the session check and
        // the GetForegroundWindow/ProcessIdToSessionId overhead on every event.
        // At 1000 Hz mouse polling this avoids ~1000 blocking kernel calls/sec on
        // Apollo's SendInput thread, which was starving the encode pipeline.
        const MSLLHOOKSTRUCT* mhs = reinterpret_cast<const MSLLHOOKSTRUCT*>(lParam);
        if (mhs->flags & LLMHF_INJECTED)
            return CallNextHookEx(g_mouseHook, nCode, wParam, lParam);

        if (!ShouldPassThrough())
            return 1;
    }

    return CallNextHookEx(g_mouseHook, nCode, wParam, lParam);
}

// ═══════════════════════════════════════════════════════════════════
//  Exported functions
// ═══════════════════════════════════════════════════════════════════

extern "C" {

int __stdcall MultiSeatHook_Install(DWORD targetSessionId)
{
    if (g_keyboardHook != NULL || g_mouseHook != NULL)
    {
        // Already installed — update target session
        g_targetSessionId = targetSessionId;
        return 0;
    }

    g_targetSessionId = targetSessionId;

    // Reset foreground cache
    g_cachedFgWindow = NULL;
    g_cachedFgSession = 0xFFFFFFFF;

    g_keyboardHook = SetWindowsHookExW(
        WH_KEYBOARD_LL,
        LowLevelKeyboardProc,
        g_hModule,
        0  // thread ID 0 = all threads (system-wide)
    );

    if (g_keyboardHook == NULL)
    {
        return (int)GetLastError();
    }

    g_mouseHook = SetWindowsHookExW(
        WH_MOUSE_LL,
        LowLevelMouseProc,
        g_hModule,
        0
    );

    if (g_mouseHook == NULL)
    {
        DWORD err = GetLastError();
        UnhookWindowsHookEx(g_keyboardHook);
        g_keyboardHook = NULL;
        return (int)err;
    }

    return 0;
}

void __stdcall MultiSeatHook_Uninstall(void)
{
    if (g_keyboardHook != NULL)
    {
        UnhookWindowsHookEx(g_keyboardHook);
        g_keyboardHook = NULL;
    }

    if (g_mouseHook != NULL)
    {
        UnhookWindowsHookEx(g_mouseHook);
        g_mouseHook = NULL;
    }

    g_targetSessionId = 0xFFFFFFFF;
    g_cachedFgWindow = NULL;
}

BOOL __stdcall MultiSeatHook_IsInstalled(void)
{
    return (g_keyboardHook != NULL && g_mouseHook != NULL) ? TRUE : FALSE;
}

DWORD __stdcall MultiSeatHook_GetTargetSession(void)
{
    return g_targetSessionId;
}

} // extern "C"

// Store module handle for SetWindowsHookEx
void SetModuleHandle(HINSTANCE hModule)
{
    g_hModule = hModule;
}
