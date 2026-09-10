using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public interface ILiveStreamingService : IDisposable
{
    event EventHandler<int>? StreamExited;

    bool IsStreaming { get; }

    Task StartAsync(LiveStreamOptions options, Action<string> onOutput, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<string> ListDevicesAsync(CancellationToken cancellationToken = default);
}
