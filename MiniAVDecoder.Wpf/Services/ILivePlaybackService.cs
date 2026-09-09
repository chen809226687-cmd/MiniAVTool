using LibVLCSharp.Shared;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public interface ILivePlaybackService : IDisposable
{
    event EventHandler<LivePlaybackStatusChangedEventArgs>? StatusChanged;

    event EventHandler<string>? LogReceived;

    MediaPlayer MediaPlayer { get; }

    bool IsPlaying { get; }

    Task PlayAsync(string url, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
