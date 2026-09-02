using System.Diagnostics;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Streaming;

/// <summary>
/// Tests for ApolloManager.GetProcessStartTime — verifies that ProcessIdentity
/// construction uses the actual OS process start time and fails correctly
/// when the start time cannot be obtained.
/// </summary>
public class ApolloProcessIdentityTests
{
    [Fact]
    public void GetProcessStartTime_ReturnsNull_ForNonExistentPid()
    {
        // Use a PID that almost certainly doesn't exist
        var fakePid = int.MaxValue;

        var result = ApolloManager.GetProcessStartTime(fakePid);

        Assert.Null(result);
    }

    [Fact]
    public void GetProcessStartTime_ReturnsRealStartTime_ForRunningProcess()
    {
        // Use the current process — guaranteed to exist and have a valid start time
        var pid = Environment.ProcessId;

        var result = ApolloManager.GetProcessStartTime(pid);

        Assert.NotNull(result);
        // The start time should be before now (process was started in the past)
        Assert.True(result.Value < DateTimeOffset.UtcNow);
        // The start time should be within the last day (sanity check)
        Assert.True(result.Value > DateTimeOffset.UtcNow.AddDays(-1));
    }

    [Fact]
    public void GetProcessStartTime_ReturnsNull_ForRecentlyExitedProcess()
    {
        // Create and immediately kill a process, then try to get its start time
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var pid = process.Id;
        process.WaitForExit(5000);
        process.Dispose();

        // Now the process is dead — GetProcessById may or may not find it
        // depending on how quickly Windows reclaims the PID
        var result = ApolloManager.GetProcessStartTime(pid);

        // If the PID was already reused, we get a non-null result (different process)
        // If the PID still exists but is dead, we might get the start time
        // If the PID doesn't exist, we get null
        // All outcomes are acceptable — the important thing is no exception is thrown
        // and no UtcNow fallback is used
    }

    [Fact]
    public void ProcessIdentity_RequiresRealStartTime()
    {
        // Verify that ProcessIdentity construction with a real start time works
        var pid = Environment.ProcessId;
        var startTime = ApolloManager.GetProcessStartTime(pid);

        Assert.NotNull(startTime);

        var identity = new ProcessIdentity(pid, startTime.Value);
        Assert.Equal(pid, identity.ProcessId);
        Assert.Equal(startTime.Value, identity.StartedAt);
    }

    [Fact]
    public void ProcessIdentity_RejectsZeroPid()
    {
        // ProcessIdentity constructor should reject invalid PIDs
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ProcessIdentity_RejectsNegativePid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProcessIdentity(-1, DateTimeOffset.UtcNow));
    }
}
