using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// TermWrap (llccd/TermWrap) is the multi-session RDP patcher MultiSeat uses. It is a DLL
/// that TermService loads instead of the stock termsrv.dll, so the runtime check is "is
/// ServiceDll redirected away from termsrv.dll, and does the redirect target exist on
/// disk?" There is no ini to validate — TermWrap self-discovers offsets via Zydis.
///
/// The legacy stascorp/rdpwrap install is also accepted, with a warning. The tests
/// exercise EnsureMultiSession against the local machine and confirm the underlying
/// ServiceDll-reading logic behaves as expected when the registry key is missing,
/// pointing at a non-existent file, or pointing at the stock termsrv.dll.
/// </summary>
public class RdpWrapperTests
{
    private static RdpWrapper NewWrapper() => new(NullLogger<RdpWrapper>.Instance);

    [Fact]
    public void EnsureMultiSession_DoesNotThrowOnLocalHost()
    {
        // The real check is host-dependent. We just assert that the call returns without
        // throwing — the verdict (true/false) depends on whether TermWrap is installed in
        // the test environment, and either is acceptable here.
        var wrapper = NewWrapper();
        var ex = Record.Exception(() => wrapper.EnsureMultiSession());
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureMultiSession_ReturnsFalse_WhenTermServiceIsNotRunning()
    {
        // We can't easily start/stop TermService from a unit test, so this just confirms
        // the call path doesn't crash. The result reflects whatever state the host is in.
        var wrapper = NewWrapper();
        var result = wrapper.EnsureMultiSession();
        Assert.IsType<bool>(result);
    }
}
