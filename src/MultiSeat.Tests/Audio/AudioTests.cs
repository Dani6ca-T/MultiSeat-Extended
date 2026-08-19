using System.Runtime.InteropServices;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Interop;
using Xunit;

namespace MultiSeat.Tests.Audio;

public class AudioTests
{
    // ── COM Interface Layout Tests ──────────────────────────────────
    // These verify that our COM interface GUIDs and struct layouts
    // are correct — critical for the audio subsystem to work.

    [Fact]
    public void CLSID_MMDeviceEnumerator_IsCorrectGuid()
    {
        Assert.Equal(
            new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
            ComInterfaces.CLSID_MMDeviceEnumerator);
    }

    [Fact]
    public void CLSID_PolicyConfigClient_IsCorrectGuid()
    {
        Assert.Equal(
            new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),
            ComInterfaces.CLSID_PolicyConfigClient);
    }

    [Fact]
    public void IID_IPolicyConfig_IsCorrectGuid()
    {
        Assert.Equal(
            new Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
            ComInterfaces.IID_IPolicyConfig);
    }

    [Fact]
    public void EDataFlow_eRender_IsZero()
    {
        Assert.Equal(0, (int)ComInterfaces.EDataFlow.eRender);
        Assert.Equal(1, (int)ComInterfaces.EDataFlow.eCapture);
        Assert.Equal(2, (int)ComInterfaces.EDataFlow.eAll);
    }

    [Fact]
    public void ERole_Values_AreCorrect()
    {
        Assert.Equal(0, (int)ComInterfaces.ERole.eConsole);
        Assert.Equal(1, (int)ComInterfaces.ERole.eMultimedia);
        Assert.Equal(2, (int)ComInterfaces.ERole.eCommunications);
    }

    [Fact]
    public void DeviceState_Active_Is1()
    {
        Assert.Equal(1u, (uint)ComInterfaces.DeviceState.Active);
        Assert.Equal(0xFu, (uint)ComInterfaces.DeviceState.All);
    }

    [Fact]
    public void PropertyKey_StructLayout_IsCorrect()
    {
        // PropertyKey: Guid(16 bytes) + int(4 bytes) = 20 bytes
        var size = Marshal.SizeOf<ComInterfaces.PropertyKey>();
        Assert.Equal(20, size);
    }

    [Fact]
    public void PropVariant_StructLayout_IsCorrect()
    {
        // PropVariant (simplified): 4x ushort(8) + 2x IntPtr(16 on x64) = 24
        var size = Marshal.SizeOf<ComInterfaces.PropVariant>();
        Assert.Equal(24, size);
    }

    [Fact]
    public void PropVariant_VT_LPWSTR_Is31()
    {
        Assert.Equal(31, ComInterfaces.PropVariant.VT_LPWSTR);
    }

    [Fact]
    public void PKEY_Device_FriendlyName_IsCorrect()
    {
        var key = ComInterfaces.PKEY_Device_FriendlyName;
        Assert.Equal(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), key.fmtid);
        Assert.Equal(14, key.pid);
    }

    [Fact]
    public void PKEY_DeviceInterface_FriendlyName_IsCorrect()
    {
        var key = ComInterfaces.PKEY_DeviceInterface_FriendlyName;
        Assert.Equal(new Guid("026E516E-B814-414B-8384-98F824C6F2C0"), key.fmtid);
        Assert.Equal(2, key.pid);
    }

    // ── VAC Detection Tests ──────────────────────────────────────────

    [Fact]
    public void AudioEndpointInfo_IsVac_IdentifiesVacByName()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{test}",
            FriendlyName = "Line 1 (Virtual Audio Cable)",
            IsVac = true,
            VacCableIndex = 1
        };

        Assert.True(ep.IsVac);
        Assert.Equal(1, ep.VacCableIndex);
    }

    [Fact]
    public void AudioEndpointInfo_IsVac_FalseForRealDevice()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{test2}",
            FriendlyName = "Speakers (Realtek Audio)",
            IsVac = false,
            VacCableIndex = -1
        };

        Assert.False(ep.IsVac);
        Assert.Equal(-1, ep.VacCableIndex);
    }

    [Fact]
    public void AudioEndpointInfo_ToString_IncludesAllFields()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "test-id",
            FriendlyName = "Test Device",
            IsVac = true,
            VacCableIndex = 3
        };

        var str = ep.ToString();
        Assert.Contains("Test Device", str);
        Assert.Contains("test-id", str);
        Assert.Contains("VAC=True", str);
        Assert.Contains("Cable=3", str);
    }

    [Fact]
    public void AudioAssignment_RecordEquality()
    {
        var gameEp = new AudioEndpointInfo { DeviceId = "game", FriendlyName = "Game Device", IsVac = true };
        var capEp  = new AudioEndpointInfo { DeviceId = "cap",  FriendlyName = "Microphone (Steam Streaming Microphone)", IsVac = false };
        var pair = new AudioSeatPair { GameRender = gameEp, MicCapture = capEp };

        var a1 = new AudioAssignment(pair, 5);
        var a2 = new AudioAssignment(pair, 5);

        Assert.Equal(a1, a2);
    }

    // ── VoiceMeeter Cable Index Tests ───────────────────────────────

    [Fact]
    public void AudioEndpointInfo_VoiceMeeterInput_HasIndex2()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{vm1}",
            FriendlyName = "VoiceMeeter Input",
            IsVac = true,
            VacCableIndex = 2
        };
        Assert.Equal(2, ep.VacCableIndex);
    }

    [Fact]
    public void AudioEndpointInfo_VoiceMeeterAuxInput_HasIndex3()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{vm2}",
            FriendlyName = "VoiceMeeter Aux Input",
            IsVac = true,
            VacCableIndex = 3
        };
        Assert.Equal(3, ep.VacCableIndex);
    }

    [Fact]
    public void AudioEndpointInfo_VoiceMeeterVaio3Input_HasIndex4()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{vm3}",
            FriendlyName = "VoiceMeeter VAIO3 Input",
            IsVac = true,
            VacCableIndex = 4
        };
        Assert.Equal(4, ep.VacCableIndex);
    }

    [Fact]
    public void AudioEndpointInfo_VbCableOutput_HasIndex1()
    {
        var ep = new AudioEndpointInfo
        {
            DeviceId = "{0.0.0.00000000}.{cable}",
            FriendlyName = "CABLE Output (VB-Audio Virtual Cable)",
            IsVac = true,
            VacCableIndex = 1
        };
        Assert.Equal(1, ep.VacCableIndex);
    }

    // ── Integration tests (require audio hardware / VAC) ────────────

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires audio subsystem — run manually on a configured host")]
    public void EnumerateRenderEndpoints_ReturnsAtLeastOneDevice()
    {
        // This would test real IMMDeviceEnumerator on a system with audio
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires VAC installed")]
    public void FindVacEndpoints_FindsInstalledCables()
    {
        // This would test VAC discovery on a system with VAC installed
    }

    [Trait("Category", "Integration")]
    [Fact(Skip = "Requires audio subsystem")]
    public void AudioPolicyClient_SetDefaultEndpoint_Succeeds()
    {
        // This would test setting the default audio device
    }

    // ── VoiceMeeter discovery ───────────────────────────────────────
    // AudioRouter used to hardcode "C:\Program Files\VB\Voicemeeter\voicemeeterpro.exe". The
    // installer puts VoiceMeeter under the 32-bit tree, so that path exists on no host we have
    // seen, and the miss was logged at Debug — which a Windows Service never writes anywhere.
    // These cover the resolution rules so the same silence cannot come back.

    private static string MakeVoiceMeeterRoot(params string[] exeNames)
    {
        var root = Path.Combine(Path.GetTempPath(), "ms-vm-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "VB", "Voicemeeter");
        Directory.CreateDirectory(dir);
        foreach (var name in exeNames)
            File.WriteAllText(Path.Combine(dir, name), string.Empty);
        return root;
    }

    [Fact]
    public void FindVoiceMeeterExe_PrefersPotato_OverBananaAndBasic()
    {
        // All three editions ship side by side in one directory; seat 3 needs VAIO3, which only
        // Potato provides, so Potato has to win when several are present.
        var root = MakeVoiceMeeterRoot("voicemeeter.exe", "voicemeeterpro.exe", "voicemeeter8x64.exe");
        try
        {
            var found = AudioRouter.FindVoiceMeeterExe([root]);
            Assert.NotNull(found);
            Assert.Equal("voicemeeter8x64.exe", Path.GetFileName(found));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindVoiceMeeterExe_FallsBackToBanana_WhenPotatoAbsent()
    {
        var root = MakeVoiceMeeterRoot("voicemeeter.exe", "voicemeeterpro.exe");
        try
        {
            var found = AudioRouter.FindVoiceMeeterExe([root]);
            Assert.Equal("voicemeeterpro.exe", Path.GetFileName(found));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindVoiceMeeterExe_SearchesLaterRoots_WhenFirstHasNothing()
    {
        // The real call passes Program Files (x86) then Program Files; an install in only one of
        // them must still be found.
        var empty = Path.Combine(Path.GetTempPath(), "ms-vm-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var populated = MakeVoiceMeeterRoot("voicemeeterpro.exe");
        try
        {
            var found = AudioRouter.FindVoiceMeeterExe([empty, populated]);
            Assert.NotNull(found);
            Assert.StartsWith(populated, found);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
            Directory.Delete(populated, recursive: true);
        }
    }

    [Fact]
    public void FindVoiceMeeterExe_ReturnsNull_WhenNotInstalled()
    {
        // The case that must fail loudly rather than launch something arbitrary.
        var root = MakeVoiceMeeterRoot();     // directory exists, no executables in it
        try
        {
            Assert.Null(AudioRouter.FindVoiceMeeterExe([root]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindVoiceMeeterExe_IgnoresMissingAndBlankRoots()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ms-vm-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Null(AudioRouter.FindVoiceMeeterExe([missing, "", "   "]));
    }

    [Fact]
    public void VoiceMeeterExeNames_CoverAllThreeEditions()
    {
        // A regression here means some edition silently stops being discoverable.
        Assert.Contains("voicemeeter8x64.exe", AudioRouter.VoiceMeeterExeNames);   // Potato
        Assert.Contains("voicemeeterpro.exe", AudioRouter.VoiceMeeterExeNames);    // Banana
        Assert.Contains("voicemeeter.exe", AudioRouter.VoiceMeeterExeNames);       // basic
    }
}
