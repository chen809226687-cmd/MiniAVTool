using LibVLCSharp.Shared;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

/// <summary>
/// 使用 LibVLC 播放直播流。
/// LibVLCSharp 只负责 .NET 封装，真正的解码和网络拉流由 VLC 原生引擎完成。
/// </summary>
public sealed class LibVlcPlaybackService : ILivePlaybackService
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly object _syncRoot = new();
    private Media? _media;
    private bool _disposed;

    public LibVlcPlaybackService()
    {
        // VideoLAN.LibVLC.Windows NuGet 包会把 libvlc.dll 和插件复制到输出目录。
        // Core.Initialize() 必须在创建 LibVLC 之前调用。
        Core.Initialize();

        // WPF VideoView 使用原生视频输出窗口。关闭硬件解码可避免部分显卡
        // 对 RTMP 摄像头流出现黑屏，但不影响 FFmpeg 推流端的编码。
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--no-osd",
            "--avcodec-hw=none");
        _mediaPlayer = new MediaPlayer(_libVlc);

        _mediaPlayer.Opening += (_, _) => RaiseStatus(LivePlaybackState.Connecting, "正在连接直播流...");
        _mediaPlayer.Buffering += (_, _) => RaiseStatus(LivePlaybackState.Buffering, "正在缓冲直播流...");
        _mediaPlayer.Playing += (_, _) => RaiseStatus(LivePlaybackState.Playing, "直播播放中");
        _mediaPlayer.Paused += (_, _) => RaiseStatus(LivePlaybackState.Paused, "播放已暂停");
        _mediaPlayer.Stopped += (_, _) => RaiseStatus(LivePlaybackState.Stopped, "播放已停止");
        _mediaPlayer.EndReached += (_, _) => RaiseStatus(LivePlaybackState.Stopped, "直播流已结束");
        _mediaPlayer.EncounteredError += (_, _) =>
        {
            RaiseLog("LibVLC 播放失败，请检查播放地址、服务器端口和推流状态。");
            RaiseStatus(LivePlaybackState.Error, "播放失败");
        };
    }

    public event EventHandler<LivePlaybackStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<string>? LogReceived;

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public bool IsPlaying => _mediaPlayer.IsPlaying;

    public Task PlayAsync(string url, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("播放地址不能为空。", nameof(url));
        }

        StopInternal();

        var normalizedUrl = url.Trim();
        RaiseLog($"开始连接：{normalizedUrl}");
        RaiseStatus(LivePlaybackState.Connecting, "正在连接直播流...");

        // FromLocation 支持 rtmp://、http://、https:// 等网络媒体地址。
        var media = new Media(_libVlc, normalizedUrl, FromType.FromLocation);
        media.AddOption(":network-caching=1000");
        media.AddOption(":live-caching=1000");
        media.AddOption(":avcodec-hw=none");

        lock (_syncRoot)
        {
            _media = media;
        }

        if (!_mediaPlayer.Play(media))
        {
            DisposeCurrentMedia();
            RaiseStatus(LivePlaybackState.Error, "播放器无法打开该地址");
            throw new InvalidOperationException("LibVLC 无法打开直播地址。");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        StopInternal();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopInternal();
        }
        finally
        {
            // MediaPlayer.Dispose() 会释放其原生事件资源，避免使用新的 lambda
            // 进行“取消订阅”（新的 lambda 并不是原来注册的委托）。
            _mediaPlayer.Dispose();
            _libVlc.Dispose();
        }
    }

    private void StopInternal()
    {
        if (_mediaPlayer.IsPlaying || _mediaPlayer.State != VLCState.Stopped)
        {
            _mediaPlayer.Stop();
        }

        DisposeCurrentMedia();
    }

    private void DisposeCurrentMedia()
    {
        Media? media;
        lock (_syncRoot)
        {
            media = _media;
            _media = null;
        }

        media?.Dispose();
    }

    private void RaiseStatus(LivePlaybackState state, string message)
    {
        StatusChanged?.Invoke(this, new LivePlaybackStatusChangedEventArgs(state, message));
    }

    private void RaiseLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }
}
