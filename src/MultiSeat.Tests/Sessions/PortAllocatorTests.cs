using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using Xunit;

namespace MultiSeat.Tests.Sessions;

public class PortAllocatorTests
{
    [Fact]
    public void Allocate_ReturnsUniquePortBlocks()
    {
        var allocator = new PortAllocator();
        var ports = new HashSet<int>();

        for (int i = 0; i < Constants.MaxSeats; i++)
        {
            var port = allocator.Allocate();
            Assert.True(ports.Add(port), $"Duplicate port base: {port}");
        }
    }

    [Fact]
    public void Allocate_ThrowsWhenExhausted()
    {
        var allocator = new PortAllocator();

        for (int i = 0; i < Constants.MaxSeats; i++)
            allocator.Allocate();

        Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
    }

    [Fact]
    public void Release_MakesPortAvailableAgain()
    {
        var allocator = new PortAllocator();
        var port = allocator.Allocate();
        allocator.Release(port);

        var port2 = allocator.Allocate();
        Assert.Equal(port, port2);
    }

    [Fact]
    public void PortOffsets_AreCorrect()
    {
        var allocator = new PortAllocator();
        var basePort = allocator.Allocate();

        Assert.Equal(basePort + 0, allocator.GetHttpsPort(basePort));
        Assert.Equal(basePort + 1, allocator.GetHttpPort(basePort));
        Assert.Equal(basePort + 2, allocator.GetVideoPort(basePort));
        Assert.Equal(basePort + 3, allocator.GetAudioPort(basePort));
        Assert.Equal(basePort + 4, allocator.GetControlPort(basePort));
    }
}
