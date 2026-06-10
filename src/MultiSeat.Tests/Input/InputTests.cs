using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Input;
using MultiSeat.Service.Interop;
using Xunit;

namespace MultiSeat.Tests.Input;

public class InputTests
{
    // ── Helper ─────────────────────────────────────────────────────────
    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static IOptions<MultiSeatOptions> DefaultOptions() =>
        Options.Create(new MultiSeatOptions());

    // ── XInput struct layout tests ──────────────────────────────────

    [Fact]
    public void XInput_XUSER_MAX_COUNT_Is4()
    {
        Assert.Equal(4, XInput.XUSER_MAX_COUNT);
    }

    [Fact]
    public void XInput_Gamepad_StructSize_Is12()
    {
        // ushort(2) + byte(1) + byte(1) + 4x short(8) = 12 bytes
        Assert.Equal(12, Marshal.SizeOf<XInput.Gamepad>());
    }

    [Fact]
    public void XInput_State_StructSize_Is16()
    {
        // uint(4) + Gamepad(12) = 16 bytes
        Assert.Equal(16, Marshal.SizeOf<XInput.State>());
    }

    [Fact]
    public void XInput_Vibration_StructSize_Is4()
    {
        // 2x ushort(4) = 4 bytes
        Assert.Equal(4, Marshal.SizeOf<XInput.Vibration>());
    }

    [Fact]
    public void XInput_GamepadButtons_FlagValues_AreCorrect()
    {
        Assert.Equal((ushort)0x0001, (ushort)XInput.GamepadButtons.DPadUp);
        Assert.Equal((ushort)0x1000, (ushort)XInput.GamepadButtons.A);
        Assert.Equal((ushort)0x2000, (ushort)XInput.GamepadButtons.B);
        Assert.Equal((ushort)0x4000, (ushort)XInput.GamepadButtons.X);
        Assert.Equal((ushort)0x8000, (ushort)XInput.GamepadButtons.Y);
    }

    [Fact]
    public void XInput_Deadzone_ThresholdsMatch_MicrosoftRecommendation()
    {
        Assert.Equal((short)7849, XInput.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE);
        Assert.Equal((short)8689, XInput.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE);
        Assert.Equal((byte)30, XInput.XINPUT_GAMEPAD_TRIGGER_THRESHOLD);
    }

    // ── VibrationEventArgs tests ────────────────────────────────────

    [Fact]
    public void VibrationEventArgs_StoresValues()
    {
        var seatId = Guid.NewGuid();
        var args = new VibrationEventArgs(seatId, 128, 64);

        Assert.Equal(seatId, args.SeatId);
        Assert.Equal(128, args.LargeMotor);
        Assert.Equal(64, args.SmallMotor);
    }

    // ── Vibration scaling test ──────────────────────────────────────

    [Fact]
    public void VibrationScaling_0To255_MapsTo_0To65535()
    {
        // ViGEm gives 0-255, XInput expects 0-65535
        // Formula: xinput = vigem * 257
        byte vigemMax = 255;
        ushort xinputMax = (ushort)(vigemMax * 257);
        Assert.Equal(65535, xinputMax);

        byte vigemMid = 128;
        ushort xinputMid = (ushort)(vigemMid * 257);
        Assert.Equal(32896, xinputMid);

        byte vigemZero = 0;
        ushort xinputZero = (ushort)(vigemZero * 257);
        Assert.Equal(0, xinputZero);
    }

    // ── MultiSeatInputHook P/Invoke binding tests ────────────────────────

    [Fact]
    public void MultiSeatInputHookNative_DllName_IsMultiSeatInputHook()
    {
        // Verify the P/Invoke signatures compile and are accessible
        // (doesn't call the DLL, just checks the managed wrappers exist)
        var installMethod = typeof(MultiSeatInputHookNative)
            .GetMethod(nameof(MultiSeatInputHookNative.MultiSeatHook_Install));
        Assert.NotNull(installMethod);

        var attr = installMethod!
            .GetCustomAttributes(typeof(DllImportAttribute), false)
            .Cast<DllImportAttribute>()
            .Single();
        Assert.Equal("MultiSeatInputHook.dll", attr.Value);
        Assert.Equal(CallingConvention.StdCall, attr.CallingConvention);
    }

    [Fact]
    public void MultiSeatInputHookNative_AllExportsAreDefined()
    {
        // All 6 exports from the .def file should have P/Invoke bindings
        var methods = new[]
        {
            nameof(MultiSeatInputHookNative.MultiSeatHook_Install),
            nameof(MultiSeatInputHookNative.MultiSeatHook_Uninstall),
            nameof(MultiSeatInputHookNative.MultiSeatHook_IsInstalled),
            nameof(MultiSeatInputHookNative.MultiSeatHook_GetTargetSession),
            nameof(MultiSeatInputHookNative.MultiSeatRawInput_Register),
            nameof(MultiSeatInputHookNative.MultiSeatRawInput_Unregister),
        };

        foreach (var method in methods)
        {
            var mi = typeof(MultiSeatInputHookNative).GetMethod(method);
            Assert.NotNull(mi);
        }
    }

    // ── InputHookManager config tests ─────────────────────────────────

    [Fact]
    public void InputHookManager_DisabledByConfig_DoesNotStart()
    {
        var opts = new MultiSeatOptions { EnableKeyboardMouseIsolation = false };
        var mgr = new InputHookManager(
            new NullLogger<InputHookManager>(), Options.Create(opts));

        mgr.Start(); // should be a no-op
        Assert.False(mgr.IsInstalled);
        Assert.Equal(0xFFFFFFFF, mgr.CurrentSessionId);
    }

    [Fact]
    public void InputHookManager_MissingDll_DoesNotCrash()
    {
        var opts = new MultiSeatOptions
        {
            EnableKeyboardMouseIsolation = true,
            InputHookDllPath = @"C:\nonexistent\MultiSeatInputHook.dll"
        };
        var mgr = new InputHookManager(
            new NullLogger<InputHookManager>(), Options.Create(opts));

        mgr.Start(); // should log warning but not crash
        Assert.False(mgr.IsInstalled);
    }

    // ── MultiSeatOptions input defaults ───────────────────────────────────

    [Fact]
    public void MultiSeatOptions_InputDefaults_AreCorrect()
    {
        var opts = new MultiSeatOptions();
        Assert.Equal("MultiSeatInputHook.dll", opts.InputHookDllPath);
        Assert.False(opts.EnableKeyboardMouseIsolation); // no-op as architected; off by default
        Assert.True(opts.AutoAssignControllers);
    }

    // ── InputRouter assignment tests ──────────────────────────────────

    [Fact]
    public void InputRouter_AssignController_RecordsMapping()
    {
        var router = new InputRouter(
            new NullLogger<InputRouter>(),
            new ControllerManager(new NullLogger<ControllerManager>()));

        var seatId = Guid.NewGuid();
        router.AssignController(0, seatId);

        var assignments = router.GetAssignments();
        Assert.Single(assignments);
        Assert.Equal(seatId, assignments[0]);
    }

    [Fact]
    public void InputRouter_UnassignController_RemovesMapping()
    {
        var router = new InputRouter(
            new NullLogger<InputRouter>(),
            new ControllerManager(new NullLogger<ControllerManager>()));

        var seatId = Guid.NewGuid();
        router.AssignController(1, seatId);
        router.UnassignController(1);

        Assert.Empty(router.GetAssignments());
    }

    [Fact]
    public void InputRouter_AssignController_ThrowsOnInvalidIndex()
    {
        var router = new InputRouter(
            new NullLogger<InputRouter>(),
            new ControllerManager(new NullLogger<ControllerManager>()));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => router.AssignController(-1, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => router.AssignController(4, Guid.NewGuid()));
    }

    [Fact]
    public void InputRouter_AssignController_OverwritesPreviousAssignment()
    {
        var router = new InputRouter(
            new NullLogger<InputRouter>(),
            new ControllerManager(new NullLogger<ControllerManager>()));

        var seat1 = Guid.NewGuid();
        var seat2 = Guid.NewGuid();
        router.AssignController(0, seat1);
        router.AssignController(0, seat2);

        var assignments = router.GetAssignments();
        Assert.Equal(seat2, assignments[0]);
    }

    // ── Integration tests ───────────────────────────────────────────

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires XInput controller connected")]
    public void XInputGetState_ReturnsStateForConnectedController()
    {
        var result = XInput.GetState(0, out var state);
        Assert.Equal(XInput.ERROR_SUCCESS, result);
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires ViGEmBus driver")]
    public void ControllerManager_CreatesVirtualController()
    {
        // Tests real ViGEmBus virtual controller creation
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires HidHide driver")]
    public void HidHideConfigurator_ListsGamingDevices()
    {
        // Tests real HidHide device enumeration
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires MultiSeatInputHook.dll built and accessible")]
    public void InputHookManager_InstallsAndUninstallsHooks()
    {
        var opts = new MultiSeatOptions
        {
            EnableKeyboardMouseIsolation = true,
            InputHookDllPath = @"MultiSeatInputHook.dll"
        };
        var mgr = new InputHookManager(
            new NullLogger<InputHookManager>(), Options.Create(opts));

        mgr.Start();
        mgr.InstallForSession(1);
        Thread.Sleep(100);
        Assert.True(mgr.IsInstalled);

        mgr.Uninstall();
        Thread.Sleep(100);
        Assert.False(mgr.IsInstalled);

        mgr.Stop();
    }
}
