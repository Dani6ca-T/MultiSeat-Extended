using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Input;

/// <summary>
/// Manages low-level keyboard and mouse hooks via MultiSeatInputHook.dll.
///
/// Installs WH_KEYBOARD_LL and WH_MOUSE_LL hooks that filter input by
/// Windows Session ID, preventing keyboard/mouse bleed between the host
/// desktop and active seat sessions.
///
/// The hook DLL runs its callbacks in the context of a dedicated background
/// thread that owns a Windows message loop. This is required because low-level
/// hooks must have a pumped message queue to receive callbacks.
///
/// Only one session can be filtered at a time at the hook level.
/// </summary>
public sealed class InputHookManager : IDisposable
{
    private readonly ILogger<InputHookManager> _logger;
    private readonly string _dllPath;
    private readonly bool _enabled;

    private Thread? _messageThread;
    private volatile bool _running;
    private uint _currentSessionId = 0xFFFFFFFF;

    // Hook install/uninstall must happen on the message pump thread (the thread that owns
    // the WH_KEYBOARD_LL hook). Actions queued here are drained by the pump thread.
    private readonly ConcurrentQueue<Action> _hookActions = new();
    private volatile uint _pumpThreadNativeId;

    public InputHookManager(
        ILogger<InputHookManager> logger,
        IOptions<MultiSeatOptions> options)
    {
        _logger = logger;
        _dllPath = options.Value.InputHookDllPath;
        _enabled = options.Value.EnableKeyboardMouseIsolation;
    }

    /// <summary>Whether hooks are currently installed and active.</summary>
    public bool IsInstalled => _running && MultiSeatInputHookNative.MultiSeatHook_IsInstalled();

    /// <summary>The session ID currently being filtered.</summary>
    public uint CurrentSessionId => _currentSessionId;

    /// <summary>Start the hook manager background thread.</summary>
    public void Start()
    {
        if (!_enabled) return;

        if (!File.Exists(_dllPath))
        {
            _logger.LogWarning(
                "MultiSeatInputHook.dll not found at {Path} — keyboard/mouse isolation unavailable. " +
                "Build MultiSeat.InputHook (CMake) and re-run install-service.ps1.",
                _dllPath);
            return;
        }

        _running = true;
        _messageThread = new Thread(MessagePump)
        {
            IsBackground = true,
            Name = "InputHookMessagePump"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        _logger.LogInformation("InputHookManager started (DLL: {Path})", _dllPath);
    }

    /// <summary>
    /// Install hooks targeting the given session.
    /// If hooks are already installed they are updated to the new session.
    /// The installation is marshalled to the message pump thread so hook
    /// callbacks are delivered to a thread that is actually pumping messages.
    /// </summary>
    public void InstallForSession(uint sessionId)
    {
        if (!_enabled || !_running) return;

        _currentSessionId = sessionId;

        // Queue the install to run on the message pump thread.
        // WH_KEYBOARD_LL callbacks are delivered via the message queue of the
        // thread that called SetWindowsHookEx. If that thread doesn't pump
        // messages, Windows removes the hook after ~300ms (LowLevelHooksTimeout).
        _hookActions.Enqueue(() =>
        {
            var result = MultiSeatInputHookNative.MultiSeatHook_Install(sessionId);
            if (result == 0)
                _logger.LogInformation("Input hooks installed — filtering for session {Sid}", sessionId);
            else
                _logger.LogError("MultiSeatHook_Install failed with Win32 error {Err} for session {Sid}", result, sessionId);
        });
        WakeMessagePump();
    }

    /// <summary>Remove hooks and stop filtering.</summary>
    public void Uninstall()
    {
        if (!_running) return;

        _currentSessionId = 0xFFFFFFFF;

        _hookActions.Enqueue(() =>
        {
            MultiSeatInputHookNative.MultiSeatHook_Uninstall();
            _logger.LogInformation("Input hooks uninstalled");
        });
        WakeMessagePump();
    }

    /// <summary>Stop the hook manager and uninstall any active hooks.</summary>
    public void Stop()
    {
        if (!_running) return;

        _running = false;

        // Uninstall hooks directly — at this point we're tearing down and don't
        // need to marshal to the pump thread. The pump thread is about to exit.
        MultiSeatInputHookNative.MultiSeatHook_Uninstall();

        // Post WM_QUIT to unblock GetMessage on the pump thread
        WakeMessagePump();

        _messageThread?.Join(3000);
        _messageThread = null;

        _logger.LogInformation("InputHookManager stopped");
    }

    public void Dispose() => Stop();

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Background thread that pumps messages so the hook callbacks fire.
    /// WH_KEYBOARD_LL and WH_MOUSE_LL callbacks are delivered via the
    /// message queue of the thread that installed them, so hooks MUST be
    /// installed from this thread — see InstallForSession / Uninstall.
    /// </summary>
    private void MessagePump()
    {
        _pumpThreadNativeId = GetCurrentThreadId();

        _logger.LogDebug("InputHook message pump started (thread {Id})",
            Thread.CurrentThread.ManagedThreadId);

        MSG msg;
        while (_running)
        {
            // Drain any pending hook install/uninstall actions first
            while (_hookActions.TryDequeue(out var action))
                action();

            // Block until a message arrives, then process it
            if (GetMessage(out msg, IntPtr.Zero, 0, 0) <= 0)
                break; // WM_QUIT or error

            if (!_running) break;

            // Drain actions again — an install may have been queued and we just
            // woke via PostThreadMessage(WM_NULL)
            while (_hookActions.TryDequeue(out var action))
                action();

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Drain any remaining actions on the way out
        while (_hookActions.TryDequeue(out var action))
            action();

        _logger.LogDebug("InputHook message pump exited");
    }

    /// <summary>Post WM_NULL to the pump thread to wake GetMessage for pending actions or shutdown.</summary>
    private void WakeMessagePump()
    {
        var tid = _pumpThreadNativeId;
        if (tid != 0)
            PostThreadMessage(tid, WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }

    private const uint WM_NULL = 0x0000;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }
}
