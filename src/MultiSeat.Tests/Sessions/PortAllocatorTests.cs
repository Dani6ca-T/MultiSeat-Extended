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

        Assert.Equal(basePort + Constants.OffsetGfeHttp, allocator.GetGfeHttpPort(basePort));
        Assert.Equal(basePort + Constants.OffsetWebUi, allocator.GetWebUiPort(basePort));
        Assert.Equal(basePort + Constants.OffsetVideo, allocator.GetVideoPort(basePort));
        Assert.Equal(basePort + Constants.OffsetAudio, allocator.GetAudioPort(basePort));
        Assert.Equal(basePort + Constants.OffsetControl, allocator.GetControlPort(basePort));
    }
}
