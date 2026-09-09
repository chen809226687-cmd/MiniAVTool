namespace MiniAVDecoder.Wpf.Models;

public sealed class LiveStreamOptions
{
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string RtmpUrl { get; init; } = string.Empty;
    public bool UseCamera { get; init; }
    public string CameraDeviceName { get; init; } = string.Empty;
    public bool IncludeAudio { get; init; }
    public string AudioDeviceName { get; init; } = string.Empty;
    public int FrameRate { get; init; } = 30;
    public string VideoSize { get; init; } = "1280x720";
    public int VideoBitrateKbps { get; init; } = 2500;
}
