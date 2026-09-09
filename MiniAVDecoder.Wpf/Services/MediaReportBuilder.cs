using System.Text;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public static class MediaReportBuilder
{
    public static string Build(MediaInfo info, string rawJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("媒体信息");
        sb.AppendLine($"输入文件：{info.InputPath}");
        sb.AppendLine($"时长：{info.DurationSeconds:F3} 秒");
        sb.AppendLine();
        sb.AppendLine("视频");
        if (info.Video.StreamIndex >= 0)
        {
            sb.AppendLine($"  流索引：{info.Video.StreamIndex}");
            sb.AppendLine($"  编码：{info.Video.Codec}");
            sb.AppendLine($"  分辨率：{info.Video.Width} x {info.Video.Height}");
            sb.AppendLine($"  帧率：{info.Video.FrameRate:F3}");
        }
        else
        {
            sb.AppendLine("  未找到视频流");
        }

        sb.AppendLine();
        sb.AppendLine("音频");
        if (info.Audio.StreamIndex >= 0)
        {
            sb.AppendLine($"  流索引：{info.Audio.StreamIndex}");
            sb.AppendLine($"  编码：{info.Audio.Codec}");
            sb.AppendLine($"  采样率：{info.Audio.SampleRate}");
            sb.AppendLine($"  声道数：{info.Audio.Channels}");
            sb.AppendLine($"  位深：{info.Audio.BitsPerSample}");
        }
        else
        {
            sb.AppendLine("  未找到音频流");
        }

        sb.AppendLine();
        sb.AppendLine("处理结果");
        sb.AppendLine($"  已抽取帧数：{info.ExtractedFrames}");
        sb.AppendLine($"  目标帧数：{info.FrameCountTarget}");
        sb.AppendLine($"  已提取音频：{info.AudioExtracted}");
        sb.AppendLine();
        sb.AppendLine("原始 JSON");
        sb.AppendLine(rawJson);
        return sb.ToString();
    }
}
