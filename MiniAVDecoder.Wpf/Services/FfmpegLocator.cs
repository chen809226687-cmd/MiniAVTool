using System.IO;

namespace MiniAVDecoder.Wpf.Services;

public static class FfmpegLocator
{
    private const string BundledFfmpegRelativePath = "Tools/ffmpeg/ffmpeg.exe";

    public static string BundledFfmpegPath =>
        Path.Combine(AppContext.BaseDirectory, BundledFfmpegRelativePath);

    public static string GetRequiredBundledFfmpegPath()
    {
        if (File.Exists(BundledFfmpegPath))
        {
            return BundledFfmpegPath;
        }

        throw new FileNotFoundException(
            "未找到项目自带的 FFmpeg，请确认 Tools\\ffmpeg\\ffmpeg.exe 已复制到程序输出目录。",
            BundledFfmpegPath);
    }
}
