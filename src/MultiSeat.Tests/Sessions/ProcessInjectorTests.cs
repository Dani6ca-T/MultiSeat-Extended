using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;
using Xunit;

namespace MultiSeat.Tests.Sessions;

public class ProcessInjectorTests
{
    [Fact]
    public void CreationFlags_HaveCorrectValues()
    {
        // Verify our flag constants match the Windows SDK values
        Assert.Equal(0x00000010u, Kernel32.CREATE_NEW_CONSOLE);
        Assert.Equal(0x08000000u, Kernel32.CREATE_NO_WINDOW);
        Assert.Equal(0x00000400u, Kernel32.CREATE_UNICODE_ENVIRONMENT);
        Assert.Equal(0x00000020u, Kernel32.NORMAL_PRIORITY_CLASS);
    }

    [Fact]
    public void TokenAccess_MaximumAllowed_IsCorrect()
    {
        Assert.Equal(0x02000000u, AdvApi.MAXIMUM_ALLOWED);
    }

    [Fact]
    public void TokenSessionId_EnumValue_Is12()
    {
        Assert.Equal(12, (int)AdvApi.TokenInformationClass.TokenSessionId);
    }

    [Fact]
    public void LogonType_Interactive_Is2()
    {
        Assert.Equal(2, AdvApi.LOGON32_LOGON_INTERACTIVE);
    }

    [Fact]
    public void SecurityImpersonationLevel_Values_AreSequential()
    {
        Assert.Equal(0, (int)AdvApi.SecurityImpersonationLevel.SecurityAnonymous);
        Assert.Equal(1, (int)AdvApi.SecurityImpersonationLevel.SecurityIdentification);
        Assert.Equal(2, (int)AdvApi.SecurityImpersonationLevel.SecurityImpersonation);
        Assert.Equal(3, (int)AdvApi.SecurityImpersonationLevel.SecurityDelegation);
    }

    [Fact]
    public void WtsConnectState_Active_IsZero()
    {
        Assert.Equal(0, (int)WtsApi.WtsConnectState.Active);
        Assert.Equal(4, (int)WtsApi.WtsConnectState.Disconnected);
    }

    [Fact]
    public void LUID_StructSize_IsCorrect()
    {
        // LUID: DWORD LowPart + LONG HighPart = 8 bytes
        Assert.Equal(8, Marshal.SizeOf<AdvApi.LUID>());
    }

    [Fact]
    public void TOKEN_PRIVILEGES_StructSize_IsCorrect()
    {
        // int PrivilegeCount(4) + padding(4) + LUID(8) + DWORD Attributes(4) + padding(4)
        // = 16 on x64 (LUID_AND_ATTRIBUTES is 12, aligned to 16 in struct)
        var size = Marshal.SizeOf<AdvApi.TOKEN_PRIVILEGES>();
        Assert.True(size >= 16, $"TOKEN_PRIVILEGES size {size} is unexpectedly small");
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires SYSTEM privileges")]
    public async Task LaunchInSession_LaunchesProcessInTargetSession()
    {
        await Task.CompletedTask;
    }
}
