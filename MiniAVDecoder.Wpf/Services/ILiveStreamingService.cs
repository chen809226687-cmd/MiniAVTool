using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public interface ILiveStreamingService
{
    event EventHandler<int>? StreamExited;

    bool IsStreaming { get; }

    Task StartAsync(LiveStreamOptions options, Action<string> onOutput, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<string> ListDevicesAsync(string ffmpegPath, CancellationToken cancellationToken = default);
}
