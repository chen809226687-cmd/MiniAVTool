using System.Text.Json.Serialization;

namespace MiniAVDecoder.Wpf.Models;

public sealed class MediaInfo
{
    [JsonPropertyName("inputPath")]
    public string InputPath { get; set; } = string.Empty;

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("video")]
    public VideoInfo Video { get; set; } = new();

    [JsonPropertyName("audio")]
    public AudioInfo Audio { get; set; } = new();

    [JsonPropertyName("extractedFrames")]
    public int ExtractedFrames { get; set; }

    [JsonPropertyName("frameCountTarget")]
    public int FrameCountTarget { get; set; }

    [JsonPropertyName("audioExtracted")]
    public bool AudioExtracted { get; set; }
}

public sealed class VideoInfo
{
    [JsonPropertyName("streamIndex")]
    public int StreamIndex { get; set; } = -1;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("frameRateNumerator")]
    public int FrameRateNumerator { get; set; }

    [JsonPropertyName("frameRateDenominator")]
    public int FrameRateDenominator { get; set; }

    [JsonPropertyName("frameRate")]
    public double FrameRate { get; set; }
}

public sealed class AudioInfo
{
    [JsonPropertyName("streamIndex")]
    public int StreamIndex { get; set; } = -1;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;

    [JsonPropertyName("sampleRate")]
    public int SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    [JsonPropertyName("bitsPerSample")]
    public int BitsPerSample { get; set; }

    [JsonPropertyName("avgBytesPerSecond")]
    public int AvgBytesPerSecond { get; set; }
}
