using System.Collections.Concurrent;
using MultiSeat.Shared;

namespace MultiSeat.Service.Streaming;

/// <summary>
/// Thread-safe port block allocator. Each seat reserves a contiguous
/// block of ports for Apollo's HTTPS, HTTP, video, audio, and control channels.
/// </summary>
public sealed class PortAllocator
{
    private readonly int _basePort;
    private readonly ConcurrentBag<int> _available = [];
    private readonly ConcurrentDictionary<int, bool> _allocated = new();

    public PortAllocator()
    {
        _basePort = Constants.PortBase;
        for (int i = 0; i < Constants.MaxSeats; i++)
            _available.Add(_basePort + i * Constants.PortsPerSeat);
    }

    public int Allocate()
    {
        if (!_available.TryTake(out var port))
            throw new InvalidOperationException("No port blocks available. All seats occupied.");

        _allocated.TryAdd(port, true);
        return port;
    }

    public void Release(int portBase)
    {
        if (_allocated.TryRemove(portBase, out _))
            _available.Add(portBase);
    }

    public int GetHttpsPort(int portBase) => portBase + Constants.OffsetHttps;
    public int GetHttpPort(int portBase) => portBase + Constants.OffsetHttp;
    public int GetVideoPort(int portBase) => portBase + Constants.OffsetVideo;
    public int GetAudioPort(int portBase) => portBase + Constants.OffsetAudio;
    public int GetControlPort(int portBase) => portBase + Constants.OffsetControl;
}
