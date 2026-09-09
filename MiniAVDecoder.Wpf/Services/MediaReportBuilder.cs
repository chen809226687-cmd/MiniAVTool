using System.Text;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public static class MediaReportBuilder
{
    public static string Build(MediaInfo info, string rawJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Media info");
        sb.AppendLine($"Input file: {info.InputPath}");
        sb.AppendLine($"Duration: {info.DurationSeconds:F3} s");
        sb.AppendLine();
        sb.AppendLine("Video");
        if (info.Video.StreamIndex >= 0)
        {
            sb.AppendLine($"  Stream index: {info.Video.StreamIndex}");
            sb.AppendLine($"  Codec: {info.Video.Codec}");
            sb.AppendLine($"  Resolution: {info.Video.Width} x {info.Video.Height}");
            sb.AppendLine($"  Frame rate: {info.Video.FrameRate:F3}");
        }
        else
        {
            sb.AppendLine("  No video stream found");
        }

        sb.AppendLine();
        sb.AppendLine("Audio");
        if (info.Audio.StreamIndex >= 0)
        {
            sb.AppendLine($"  Stream index: {info.Audio.StreamIndex}");
            sb.AppendLine($"  Codec: {info.Audio.Codec}");
            sb.AppendLine($"  Sample rate: {info.Audio.SampleRate}");
            sb.AppendLine($"  Channels: {info.Audio.Channels}");
            sb.AppendLine($"  Bits per sample: {info.Audio.BitsPerSample}");
        }
        else
        {
            sb.AppendLine("  No audio stream found");
        }

        sb.AppendLine();
        sb.AppendLine("Processing result");
        sb.AppendLine($"  Extracted frames: {info.ExtractedFrames}");
        sb.AppendLine($"  Target frames: {info.FrameCountTarget}");
        sb.AppendLine($"  Audio extracted: {info.AudioExtracted}");
        sb.AppendLine();
        sb.AppendLine("Raw JSON");
        sb.AppendLine(rawJson);
        return sb.ToString();
    }
}
