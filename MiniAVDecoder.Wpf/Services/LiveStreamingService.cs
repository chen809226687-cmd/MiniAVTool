using System.Diagnostics;
using System.Text;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public sealed class LiveStreamingService : ILiveStreamingService, IDisposable
{
    private readonly object _syncRoot = new();
    private Process? _process;

    public event EventHandler<int>? StreamExited;

    public bool IsStreaming
    {
        get
        {
            lock (_syncRoot)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public Task StartAsync(LiveStreamOptions options, Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onOutput);

        lock (_syncRoot)
        {
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("直播已经在运行中。");
            }

            var process = CreateFfmpegProcess(options, onOutput);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => HandleProcessExited(process);

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("启动 FFmpeg 失败。");
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            onOutput("FFmpeg 已启动，正在推流到 " + options.RtmpUrl);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_syncRoot)
        {
            process = _process;
        }

        if (process == null)
        {
            return;
        }

        if (!process.HasExited)
        {
            try
            {
                // FFmpeg 收到 stdin 的 "q" 会优雅退出，并写完整输出尾部信息。
                await process.StandardInput.WriteLineAsync("q")
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
        }

        lock (_syncRoot)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }

        process.Dispose();
    }

    public async Task<string> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegLocator.GetRequiredBundledFfmpegPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-list_devices");
        psi.ArgumentList.Add("true");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("dshow");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("dummy");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("启动 FFmpeg 失败。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = new StringBuilder();
        output.Append(await stdoutTask);
        output.Append(await stderrTask);
        return output.ToString();
    }

    public void Dispose()
    {
        // 关闭窗口时必须等待 FFmpeg 退出，否则宿主进程结束后 FFmpeg
        // 仍可能继续作为独立进程推流。
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // 释放阶段不能阻止窗口关闭；StopAsync 已在超时时尝试强制结束进程树。
        }
    }

    private static Process CreateFfmpegProcess(LiveStreamOptions options, Action<string> onOutput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegLocator.GetRequiredBundledFfmpegPath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in BuildPushArguments(options))
        {
            psi.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, args) => WriteOutput(args.Data, onOutput);
        process.ErrorDataReceived += (_, args) => WriteOutput(args.Data, onOutput);
        return process;
    }

    private static IEnumerable<string> BuildPushArguments(LiveStreamOptions options)
    {
        var frameRate = Math.Clamp(options.FrameRate, 1, 60);
        var bitrate = Math.Clamp(options.VideoBitrateKbps, 300, 20000);
        var gop = Math.Max(frameRate * 2, 30);

        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "info";

        if (options.VideoSourceMode == LiveVideoSourceMode.Camera)
        {
            yield return "-f";
            yield return "dshow";
            yield return "-framerate";
            yield return frameRate.ToString();
            yield return "-i";
            yield return BuildDirectShowInput(options);
        }
        else
        {
            yield return "-f";
            yield return "gdigrab";
            yield return "-framerate";
            yield return frameRate.ToString();
            yield return "-draw_mouse";
            yield return "1";
            yield return "-i";
            yield return "desktop";

            if (options.IncludeAudio && !string.IsNullOrWhiteSpace(options.AudioDeviceName))
            {
                yield return "-f";
                yield return "dshow";
                yield return "-i";
                yield return "audio=" + options.AudioDeviceName.Trim();
            }
        }

        // 分辨率是推流输出尺寸，而不是桌面或摄像头的采集尺寸。
        // 通过等比缩放和补黑边避免不同屏幕比例下画面变形。
        var outputSize = ParseVideoSize(options.VideoSize);
        yield return "-vf";
        yield return $"scale={outputSize.Width}:{outputSize.Height}:force_original_aspect_ratio=decrease," +
                     $"pad={outputSize.Width}:{outputSize.Height}:(ow-iw)/2:(oh-ih)/2:color=black";
        yield return "-c:v";
        yield return "libx264";
        yield return "-preset";
        yield return "veryfast";
        yield return "-tune";
        yield return "zerolatency";
        yield return "-pix_fmt";
        yield return "yuv420p";
        yield return "-b:v";
        yield return bitrate + "k";
        yield return "-maxrate";
        yield return bitrate + "k";
        yield return "-bufsize";
        yield return bitrate * 2 + "k";
        yield return "-g";
        yield return gop.ToString();

        if (options.IncludeAudio && !string.IsNullOrWhiteSpace(options.AudioDeviceName))
        {
            yield return "-c:a";
            yield return "aac";
            yield return "-b:a";
            yield return "128k";
            yield return "-ar";
            yield return "44100";
        }
        else
        {
            yield return "-an";
        }

        yield return "-f";
        yield return "flv";
        yield return options.RtmpUrl.Trim();
    }

    private static (int Width, int Height) ParseVideoSize(string? videoSize)
    {
        if (!string.IsNullOrWhiteSpace(videoSize))
        {
            var parts = videoSize.Trim().ToLowerInvariant().Split('x', 2);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var width)
                && int.TryParse(parts[1], out var height)
                && width >= 320
                && height >= 180
                && width <= 2560
                && height <= 1440)
            {
                return (width, height);
            }
        }

        return (1280, 720);
    }

    private static string BuildDirectShowInput(LiveStreamOptions options)
    {
        var input = "video=" + options.CameraDeviceName.Trim();
        if (options.IncludeAudio && !string.IsNullOrWhiteSpace(options.AudioDeviceName))
        {
            input += ":audio=" + options.AudioDeviceName.Trim();
        }

        return input;
    }

    private static void WriteOutput(string? line, Action<string> onOutput)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            onOutput(line);
        }
    }

    private void HandleProcessExited(Process process)
    {
        var exitCode = process.ExitCode;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }

        StreamExited?.Invoke(this, exitCode);
    }
}
