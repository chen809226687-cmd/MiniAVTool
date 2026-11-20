using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public interface IRtcPlaybackService : IDisposable
{
    event EventHandler<LivePlaybackStatusChangedEventArgs>? StatusChanged;

    event EventHandler<string>? LogReceived;

    event EventHandler? FullscreenToggleRequested;

    bool IsPlaying { get; }

    Task PlayAsync(
        string whepUrl,
        string authorizationToken = "",
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
