using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public sealed class LiveStreamingService : ILiveStreamingService, IDisposable
{
    private const int GracefulExitTimeoutMilliseconds = 1500;
    private const int ForcedExitTimeoutMilliseconds = 3000;
    private static readonly Lazy<HardwareEncoder?> PreferredHardwareEncoder =
        new(DetectHardwareEncoder);

    private readonly object _syncRoot = new();
    private Process? _process;
    private WindowsJobObject? _processJob;

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

            var processJob = WindowsJobObject.CreateKillOnCloseJob();
            try
            {
                processJob.AssignProcess(process);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                process.Dispose();
                processJob.Dispose();
                throw;
            }

            _process = process;
            _processJob = processJob;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var output = ResolvePushOutput(options);
            var protocolName = GetProtocolDisplayName(options);
            var encoderName = PreferredHardwareEncoder.Value?.Codec ?? "libx264";
            onOutput($"FFmpeg 已启动，正在通过 {protocolName} 推流到 {output.Url}");
            onOutput($"视频编码器：{encoderName}。");
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
                    .WaitAsync(TimeSpan.FromMilliseconds(GracefulExitTimeoutMilliseconds), cancellationToken)
                    .ConfigureAwait(false);
                if (!process.WaitForExit(GracefulExitTimeoutMilliseconds))
                {
                    KillProcessTree(process);
                    process.WaitForExit(ForcedExitTimeoutMilliseconds);
                }
            }
            catch
            {
                if (!process.HasExited)
                {
                    KillProcessTree(process);
                    process.WaitForExit(ForcedExitTimeoutMilliseconds);
                }
            }
        }

        lock (_syncRoot)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                DisposeProcessJob();
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
        var outputSize = ParseVideoSize(options.VideoSize);
        var output = ResolvePushOutput(options);
        var hardwareEncoder = PreferredHardwareEncoder.Value;
        var hardwarePixelFormat = hardwareEncoder?.Codec == "h264_amf" ? "nv12" : "yuv420p";
        var includeAudio = options.IncludeAudio && !string.IsNullOrWhiteSpace(options.AudioDeviceName);

        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "info";

        if (options.VideoSourceMode == LiveVideoSourceMode.Camera)
        {
            var captureRate = Math.Min(frameRate, 30);
            var captureSize = outputSize.Width >= 1920 || outputSize.Height >= 1080
                ? (Width: 1920, Height: 1080)
                : (Width: 1280, Height: 720);

            yield return "-f";
            yield return "dshow";
            yield return "-framerate";
            yield return captureRate.ToString();
            yield return "-video_size";
            yield return $"{captureSize.Width}x{captureSize.Height}";
            yield return "-vcodec";
            yield return "mjpeg";
            yield return "-rtbufsize";
            yield return "256M";
            yield return "-i";
            yield return BuildDirectShowInput(options);

            yield return "-vf";
            yield return BuildVideoFilter(
                outputSize,
                hardwareEncoder == null ? "yuv420p" : hardwarePixelFormat);
        }
        else
        {
            // Desktop Duplication 只抓主显示器，避免每次把 7680x2160 的双屏桌面
            // 拷贝到系统内存后再由 CPU 缩小。
            yield return "-filter_complex";
            yield return
                $"ddagrab=output_idx=0:draw_mouse=1:framerate={frameRate}," +
                "hwdownload,format=bgra," +
                $"scale={outputSize.Width}:{outputSize.Height}:force_original_aspect_ratio=decrease:flags=fast_bilinear," +
                $"pad={outputSize.Width}:{outputSize.Height}:(ow-iw)/2:(oh-ih)/2:color=black," +
                $"format={(hardwareEncoder == null ? "yuv420p" : hardwarePixelFormat)}[vout]";

            if (includeAudio)
            {
                yield return "-f";
                yield return "dshow";
                yield return "-rtbufsize";
                yield return "64M";
                yield return "-i";
                yield return "audio=" + options.AudioDeviceName.Trim();
            }

            yield return "-map";
            yield return "[vout]";
            if (includeAudio)
            {
                yield return "-map";
                yield return "0:a:0";
            }
        }

        if (hardwareEncoder == null)
        {
            yield return "-c:v";
            yield return "libx264";
            yield return "-preset";
            yield return "veryfast";
            yield return "-tune";
            yield return "zerolatency";
            if (options.Protocol == LiveStreamProtocol.Rtc)
            {
                yield return "-profile:v";
                yield return "baseline";
                yield return "-bf";
                yield return "0";
            }

            yield return "-pix_fmt";
            yield return "yuv420p";
        }
        else
        {
            yield return "-c:v";
            yield return hardwareEncoder.Codec;
            if (hardwareEncoder.Codec == "h264_amf")
            {
                yield return "-usage";
                yield return "lowlatency";
                yield return "-quality";
                yield return "speed";
                yield return "-rc";
                yield return "cbr";
                yield return "-max_b_frames";
                yield return "0";
                yield return "-profile:v";
                yield return "constrained_baseline";
            }
            else
            {
                yield return "-preset";
                yield return "p1";
                yield return "-tune";
                yield return "ll";
                yield return "-rc";
                yield return "cbr";
                yield return "-bf";
                yield return "0";
                yield return "-profile:v";
                yield return "baseline";
            }

            yield return "-pix_fmt";
            yield return hardwarePixelFormat;
        }

        yield return "-b:v";
        yield return bitrate + "k";
        yield return "-maxrate";
        yield return bitrate + "k";
        yield return "-bufsize";
        yield return bitrate * 2 + "k";
        yield return "-g";
        yield return gop.ToString();
        yield return "-fps_mode";
        yield return "cfr";

        if (includeAudio)
        {
            yield return "-c:a";
            yield return output.UsesDirectWhip ? "libopus" : "aac";
            yield return "-b:a";
            yield return "128k";
            yield return "-ar";
            yield return output.UsesDirectWhip ? "48000" : "44100";

            if (output.UsesDirectWhip)
            {
                yield return "-ac";
                yield return "2";
            }
        }
        else
        {
            yield return "-an";
        }

        if (output.UsesDirectWhip)
        {
            if (!string.IsNullOrWhiteSpace(options.AuthorizationToken))
            {
                yield return "-authorization";
                yield return NormalizeAuthorizationToken(options.AuthorizationToken);
            }

            yield return "-handshake_timeout";
            yield return "10000";
        }

        yield return "-f";
        yield return output.ContainerFormat;
        yield return output.Url;
    }

    private static string BuildVideoFilter((int Width, int Height) outputSize, string pixelFormat)
    {
        return
            $"scale={outputSize.Width}:{outputSize.Height}:force_original_aspect_ratio=decrease:flags=fast_bilinear," +
            $"pad={outputSize.Width}:{outputSize.Height}:(ow-iw)/2:(oh-ih)/2:color=black," +
            $"format={pixelFormat}";
    }

    private static PushOutput ResolvePushOutput(LiveStreamOptions options)
    {
        if (options.Protocol == LiveStreamProtocol.Rtc
            && TryBuildSrsRtmpUrl(options, out var rtmpUrl))
        {
            return new PushOutput(rtmpUrl, "flv", UsesDirectWhip: false, UsesSrsRtcBridge: true);
        }

        return options.Protocol == LiveStreamProtocol.Rtc
            ? new PushOutput(options.PushUrl.Trim(), "whip", UsesDirectWhip: true, UsesSrsRtcBridge: false)
            : new PushOutput(options.PushUrl.Trim(), "flv", UsesDirectWhip: false, UsesSrsRtcBridge: false);
    }

    private static bool TryBuildSrsRtmpUrl(LiveStreamOptions options, out string rtmpUrl)
    {
        rtmpUrl = string.Empty;
        if (!Uri.TryCreate(options.PushUrl?.Trim(), UriKind.Absolute, out var whpUri)
            || !whpUri.AbsolutePath.Contains("/rtc/v1/whip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var app = GetQueryParameter(whpUri, "app");
        var stream = GetQueryParameter(whpUri, "stream");
        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(stream))
        {
            return false;
        }

        var extraQuery = new List<string>();
        foreach (var pair in whpUri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(key), "app", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Uri.UnescapeDataString(key), "stream", StringComparison.OrdinalIgnoreCase))
            {
                extraQuery.Add(pair);
            }
        }

        var builder = new UriBuilder(whpUri)
        {
            Scheme = "rtmp",
            Port = 1935,
            Path = $"/{Uri.EscapeDataString(app)}/{Uri.EscapeDataString(stream)}",
            Query = string.Join("&", extraQuery)
        };
        rtmpUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(
                    Uri.UnescapeDataString(key),
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return separator >= 0
                ? Uri.UnescapeDataString(pair[(separator + 1)..])
                : string.Empty;
        }

        return null;
    }

    private static HardwareEncoder? DetectHardwareEncoder()
    {
        foreach (var candidate in new[]
                 {
                     new HardwareEncoder("h264_nvenc", "nv12"),
                     new HardwareEncoder("h264_amf", "nv12")
                 })
        {
            if (CanUseHardwareEncoder(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CanUseHardwareEncoder(HardwareEncoder encoder)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegLocator.GetRequiredBundledFfmpegPath(),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
                 {
                     "-hide_banner",
                     "-loglevel", "error",
                     "-f", "lavfi",
                     "-i", "color=c=black:s=320x180:r=1",
                     "-frames:v", "1",
                     "-an",
                     "-c:v", encoder.Codec
                 })
        {
            psi.ArgumentList.Add(argument);
        }

        if (encoder.Codec == "h264_nvenc")
        {
            psi.ArgumentList.Add("-preset");
            psi.ArgumentList.Add("p1");
        }
        else
        {
            psi.ArgumentList.Add("-usage");
            psi.ArgumentList.Add("lowlatency");
            psi.ArgumentList.Add("-quality");
            psi.ArgumentList.Add("speed");
        }

        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add(encoder.PixelFormat);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("NUL");

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                KillProcessTree(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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

    private static string NormalizeAuthorizationToken(string token)
    {
        var value = token.Trim();
        const string bearerPrefix = "Bearer ";
        return value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[bearerPrefix.Length..].Trim()
            : value;
    }

    private static string GetProtocolDisplayName(LiveStreamOptions options)
    {
        if (options.Protocol == LiveStreamProtocol.Rtc)
        {
            return TryBuildSrsRtmpUrl(options, out _)
                ? "RTC (SRS bridge)"
                : "RTC (WHIP)";
        }

        return "RTMP";
    }

    private sealed record HardwareEncoder(string Codec, string PixelFormat);

    private sealed record PushOutput(
        string Url,
        string ContainerFormat,
        bool UsesDirectWhip,
        bool UsesSrsRtcBridge);

    private static void WriteOutput(string? line, Action<string> onOutput)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            onOutput(line);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process may have exited between the check and the kill call.
        }
    }

    private void DisposeProcessJob()
    {
        _processJob?.Dispose();
        _processJob = null;
    }

    private void HandleProcessExited(Process process)
    {
        var exitCode = process.ExitCode;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                DisposeProcessJob();
            }
        }

        StreamExited?.Invoke(this, exitCode);
    }
}

internal sealed class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private readonly SafeFileHandle _handle;

    private WindowsJobObject(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static WindowsJobObject CreateKillOnCloseJob()
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new InvalidOperationException("CreateJobObject failed.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var infoPointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, infoPointer, fDeleteOld: false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, infoPointer, (uint)length))
            {
                throw new InvalidOperationException("SetInformationJobObject failed.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infoPointer);
        }

        return new WindowsJobObject(handle);
    }

    public void AssignProcess(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new InvalidOperationException("AssignProcessToJobObject failed.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
