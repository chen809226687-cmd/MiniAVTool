using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniAVDecoder.Wpf.Infrastructure;
using MiniAVDecoder.Wpf.Models;
using MiniAVDecoder.Wpf.Services;

namespace MiniAVDecoder.Wpf.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IBackendService _backendService;
    private readonly IDialogService _dialogService;
    private readonly ILiveStreamingService _liveStreamingService;
    private readonly ILivePlaybackService _livePlaybackService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly StringBuilder _logBuilder = new();
    private readonly StringBuilder _liveLogBuilder = new();
    private readonly StringBuilder _playbackLogBuilder = new();

    private string _inputPath = string.Empty;
    private string _outputPath = string.Empty;
    private string _statusText = "就绪";
    private string _logText = string.Empty;
    private int _frameCount = 30;
    private bool _isBusy;
  
    private string _rtmpUrl = "rtmp://111.229.145.58/live/test";
    private bool _useCamera;
    private string _cameraDeviceName = string.Empty;
    private bool _includeAudio;
    private string _audioDeviceName = string.Empty;
    private int _liveFrameRate = 30;
    private string _liveVideoSize = "1280x720";
    private int _liveVideoBitrateKbps = 2500;
    private string _liveStatusText = "直播未开始";
    private string _liveLogText = string.Empty;
    private bool _isLiveStreaming;
    private string _playbackUrl = "rtmp://111.229.145.58/live/test";
    private string _playbackStatusText = "未播放";
    private string _playbackLogText = string.Empty;
    private bool _isPlaybackActive;

    private readonly RelayCommand _chooseVideoCommand;
    private readonly RelayCommand _chooseOutputCommand;
    private readonly RelayCommand _openOutputCommand;
    private readonly AsyncRelayCommand _analyzeCommand;
    private readonly AsyncRelayCommand _extractFramesCommand;
    private readonly AsyncRelayCommand _extractAudioCommand;
    private readonly AsyncRelayCommand _runAllCommand;
    private readonly AsyncRelayCommand _startLiveCommand;
    private readonly AsyncRelayCommand _stopLiveCommand;
    private readonly AsyncRelayCommand _listLiveDevicesCommand;
    private readonly AsyncRelayCommand _startPlaybackCommand;
    private readonly AsyncRelayCommand _stopPlaybackCommand;

    public MainViewModel()
        : this(
            new BackendService(),
            new DialogService(),
            new LiveStreamingService(),
            new LibVlcPlaybackService())
    {
    }

    public MainViewModel(IBackendService backendService, IDialogService dialogService, ILiveStreamingService liveStreamingService)
        : this(backendService, dialogService, liveStreamingService, new LibVlcPlaybackService())
    {
    }

    public MainViewModel(
        IBackendService backendService,
        IDialogService dialogService,
        ILiveStreamingService liveStreamingService,
        ILivePlaybackService livePlaybackService)
    {
        _backendService = backendService;
        _dialogService = dialogService;
        _liveStreamingService = liveStreamingService;
        _livePlaybackService = livePlaybackService;
        _liveStreamingService.StreamExited += OnLiveStreamExited;
        _livePlaybackService.StatusChanged += OnPlaybackStatusChanged;
        _livePlaybackService.LogReceived += OnPlaybackLogReceived;

        _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniAVDecoderOutput");
        FramePreviews = new ObservableCollection<ImageSource>();

        _chooseVideoCommand = new RelayCommand(ChooseVideo, CanUseDialogs);
        _chooseOutputCommand = new RelayCommand(ChooseOutput, CanUseDialogs);
        _openOutputCommand = new RelayCommand(OpenOutput, CanOpenOutput);
        _analyzeCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("analyze", refreshFrames: false), CanRunBackend);
        _extractFramesCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("extract-frames", refreshFrames: true), CanRunBackend);
        _extractAudioCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("extract-audio", refreshFrames: false), CanRunBackend);
        _runAllCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("all", refreshFrames: true), CanRunBackend);
        _startLiveCommand = new AsyncRelayCommand(StartLiveAsync, CanStartLive);
        _stopLiveCommand = new AsyncRelayCommand(StopLiveAsync, CanStopLive);
        _listLiveDevicesCommand = new AsyncRelayCommand(ListLiveDevicesAsync, CanListLiveDevices);
        _startPlaybackCommand = new AsyncRelayCommand(StartPlaybackAsync, CanStartPlayback);
        _stopPlaybackCommand = new AsyncRelayCommand(StopPlaybackAsync, CanStopPlayback);

        AppendLog("媒体工具已就绪。");
        AppendLiveLog("直播工具已就绪，请先确认服务器上的 SRS 或 RTMP 服务已启动。");
        AppendLiveLog("程序将使用项目自带的 FFmpeg：Tools\\ffmpeg\\ffmpeg.exe。");
        AppendPlaybackLog("观看直播功能已就绪。");
        OutputPath = _outputPath;
    }

    public string InputPath
    {
        get => _inputPath;
        set
        {
            if (SetProperty(ref _inputPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetProperty(ref _outputPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int FrameCount
    {
        get => _frameCount;
        set => SetProperty(ref _frameCount, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string RtmpUrl
    {
        get => _rtmpUrl;
        set
        {
            if (SetProperty(ref _rtmpUrl, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool UseCamera
    {
        get => _useCamera;
        set
        {
            if (SetProperty(ref _useCamera, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string CameraDeviceName
    {
        get => _cameraDeviceName;
        set
        {
            if (SetProperty(ref _cameraDeviceName, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IncludeAudio
    {
        get => _includeAudio;
        set
        {
            if (SetProperty(ref _includeAudio, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string AudioDeviceName
    {
        get => _audioDeviceName;
        set
        {
            if (SetProperty(ref _audioDeviceName, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int LiveFrameRate
    {
        get => _liveFrameRate;
        set => SetProperty(ref _liveFrameRate, value);
    }

    public string LiveVideoSize
    {
        get => _liveVideoSize;
        set => SetProperty(ref _liveVideoSize, value);
    }

    public int LiveVideoBitrateKbps
    {
        get => _liveVideoBitrateKbps;
        set => SetProperty(ref _liveVideoBitrateKbps, value);
    }

    public string LiveStatusText
    {
        get => _liveStatusText;
        private set => SetProperty(ref _liveStatusText, value);
    }

    public string LiveLogText
    {
        get => _liveLogText;
        private set => SetProperty(ref _liveLogText, value);
    }

    public bool IsLiveStreaming
    {
        get => _isLiveStreaming;
        private set
        {
            if (SetProperty(ref _isLiveStreaming, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string PlaybackUrl
    {
        get => _playbackUrl;
        set
        {
            if (SetProperty(ref _playbackUrl, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string PlaybackStatusText
    {
        get => _playbackStatusText;
        private set => SetProperty(ref _playbackStatusText, value);
    }

    public string PlaybackLogText
    {
        get => _playbackLogText;
        private set => SetProperty(ref _playbackLogText, value);
    }

    public bool IsPlaybackActive
    {
        get => _isPlaybackActive;
        private set
        {
            if (SetProperty(ref _isPlaybackActive, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ObservableCollection<ImageSource> FramePreviews { get; }

    public RelayCommand ChooseVideoCommand => _chooseVideoCommand;
    public RelayCommand ChooseOutputCommand => _chooseOutputCommand;
    public RelayCommand OpenOutputCommand => _openOutputCommand;
    public AsyncRelayCommand AnalyzeCommand => _analyzeCommand;
    public AsyncRelayCommand ExtractFramesCommand => _extractFramesCommand;
    public AsyncRelayCommand ExtractAudioCommand => _extractAudioCommand;
    public AsyncRelayCommand RunAllCommand => _runAllCommand;
    public AsyncRelayCommand StartLiveCommand => _startLiveCommand;
    public AsyncRelayCommand StopLiveCommand => _stopLiveCommand;
    public AsyncRelayCommand ListLiveDevicesCommand => _listLiveDevicesCommand;
    public AsyncRelayCommand StartPlaybackCommand => _startPlaybackCommand;
    public AsyncRelayCommand StopPlaybackCommand => _stopPlaybackCommand;

    public void Dispose()
    {
        _liveStreamingService.StreamExited -= OnLiveStreamExited;
        _livePlaybackService.StatusChanged -= OnPlaybackStatusChanged;
        _livePlaybackService.LogReceived -= OnPlaybackLogReceived;
        _ = _liveStreamingService.StopAsync();
        _livePlaybackService.Dispose();
    }

    private void ChooseVideo()
    {
        var file = _dialogService.PickVideoFile();
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        InputPath = file;
        AppendLog($"已选择视频：{file}");
    }

    private void ChooseOutput()
    {
        var folder = _dialogService.PickFolder(OutputPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        OutputPath = folder;
        AppendLog($"输出目录：{folder}");
    }

    private void OpenOutput()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            return;
        }

        Directory.CreateDirectory(OutputPath);
        _dialogService.OpenFolder(OutputPath);
    }

    private bool CanUseDialogs() => !IsBusy;
    private bool CanOpenOutput() => !IsBusy && !string.IsNullOrWhiteSpace(OutputPath);
    private bool CanRunBackend() => !IsBusy && !IsLiveStreaming && File.Exists(InputPath);
    private bool CanStartLive() => !IsLiveStreaming && !string.IsNullOrWhiteSpace(RtmpUrl) && (!UseCamera || !string.IsNullOrWhiteSpace(CameraDeviceName));
    private bool CanStopLive() => IsLiveStreaming;
    private bool CanListLiveDevices() => !IsLiveStreaming;
    private bool CanStartPlayback() => !IsPlaybackActive && IsSupportedPlaybackUrl(PlaybackUrl);
    private bool CanStopPlayback() => IsPlaybackActive;

    private async Task ExecuteBackendAsync(string command, bool refreshFrames)
    {
        if (!ValidateInputs())
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _backendService.RunAsync(command, InputPath, OutputPath, FrameCount);
            AppendProcessOutput(result);

            if (!result.Success)
            {
                StatusText = $"后端退出码：{result.ExitCode}";
                AppendLog($"后端执行失败：{result.ExecutablePath}");
                return;
            }

            StatusText = $"执行完成：{GetCommandDisplayName(command)}";
            await LoadMediaInfoAsync();

            if (refreshFrames)
            {
                RefreshFramePreviews();
            }
        }
        catch (Exception ex)
        {
            StatusText = "执行失败";
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartLiveAsync()
    {
        if (!ValidateLiveSettings())
        {
            return;
        }

        try
        {
            IsLiveStreaming = true;
            LiveStatusText = "正在启动直播...";
            await _liveStreamingService.StartAsync(BuildLiveOptions(), AppendLiveLogThreadSafe);
            LiveStatusText = "直播中";
        }
        catch (Exception ex)
        {
            IsLiveStreaming = false;
            LiveStatusText = "直播启动失败";
            AppendLiveLog(ex.Message);
        }
    }

    private async Task StopLiveAsync()
    {
        try
        {
            LiveStatusText = "正在停止直播...";
            await _liveStreamingService.StopAsync();
            IsLiveStreaming = false;
            LiveStatusText = "直播已停止";
            AppendLiveLog("直播已停止。");
        }
        catch (Exception ex)
        {
            LiveStatusText = "停止失败";
            AppendLiveLog(ex.Message);
        }
    }

    private async Task ListLiveDevicesAsync()
    {
        try
        {
            LiveStatusText = "正在读取 DirectShow 设备...";
            var output = await _liveStreamingService.ListDevicesAsync();
            AppendLiveSection("FFmpeg DirectShow 设备列表", output.TrimEnd());
            LiveStatusText = "设备列表已读取";
        }
        catch (Exception ex)
        {
            LiveStatusText = "读取设备失败";
            AppendLiveLog(ex.Message);
        }
    }

    private async Task StartPlaybackAsync()
    {
        if (!ValidatePlaybackSettings())
        {
            return;
        }

        try
        {
            IsPlaybackActive = true;
            PlaybackStatusText = "正在连接...";
            await _livePlaybackService.PlayAsync(PlaybackUrl);
        }
        catch (Exception ex)
        {
            IsPlaybackActive = false;
            PlaybackStatusText = "播放启动失败";
            AppendPlaybackLog(ex.Message);
        }
    }

    private async Task StopPlaybackAsync()
    {
        try
        {
            PlaybackStatusText = "正在停止...";
            await _livePlaybackService.StopAsync();
            IsPlaybackActive = false;
            PlaybackStatusText = "已停止";
            AppendPlaybackLog("已停止播放。");
        }
        catch (Exception ex)
        {
            PlaybackStatusText = "停止失败";
            AppendPlaybackLog(ex.Message);
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(InputPath) || !File.Exists(InputPath))
        {
            AppendLog("请先选择一个有效的视频文件。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniAVDecoderOutput");
        }

        Directory.CreateDirectory(OutputPath);
        return true;
    }

    private bool ValidateLiveSettings()
    {
        if (string.IsNullOrWhiteSpace(RtmpUrl) || !RtmpUrl.Trim().StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
        {
            AppendLiveLog("请输入 RTMP 推流地址，例如 rtmp://127.0.0.1/live/test。");
            return false;
        }

        if (UseCamera && string.IsNullOrWhiteSpace(CameraDeviceName))
        {
            AppendLiveLog("摄像头模式需要填写 DirectShow 视频设备名，请先点击“列出设备”。");
            return false;
        }

        if (IncludeAudio && string.IsNullOrWhiteSpace(AudioDeviceName))
        {
            AppendLiveLog("已勾选音频但未填写麦克风设备名，将只推送视频。");
            IncludeAudio = false;
        }

        return true;
    }

    private bool ValidatePlaybackSettings()
    {
        if (!IsSupportedPlaybackUrl(PlaybackUrl))
        {
            AppendPlaybackLog("请输入有效的直播地址，例如 rtmp://111.229.145.58/live/test。");
            return false;
        }

        return true;
    }

    private static bool IsSupportedPlaybackUrl(string? url)
    {
        return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));
    }

    private LiveStreamOptions BuildLiveOptions()
    {
        return new LiveStreamOptions
        {
            RtmpUrl = RtmpUrl,
            UseCamera = UseCamera,
            CameraDeviceName = CameraDeviceName,
            IncludeAudio = IncludeAudio,
            AudioDeviceName = AudioDeviceName,
            FrameRate = LiveFrameRate,
            VideoSize = LiveVideoSize,
            VideoBitrateKbps = LiveVideoBitrateKbps
        };
    }

    private static string GetCommandDisplayName(string command)
    {
        return command switch
        {
            "analyze" => "分析",
            "extract-frames" => "抽取帧",
            "extract-audio" => "提取音频",
            "all" => "全部执行",
            _ => command
        };
    }

    private async Task LoadMediaInfoAsync()
    {
        var jsonPath = Path.Combine(OutputPath, "media_info.json");
        if (!File.Exists(jsonPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8);
            var info = JsonSerializer.Deserialize<MediaInfo>(json, _jsonOptions);
            if (info == null)
            {
                return;
            }

            AppendSection("媒体信息", MediaReportBuilder.Build(info, json));
        }
        catch (Exception ex)
        {
            AppendLog($"读取 media_info.json 失败：{ex.Message}");
        }
    }

    private void RefreshFramePreviews()
    {
        FramePreviews.Clear();

        if (!Directory.Exists(OutputPath))
        {
            return;
        }

        var frames = Directory.GetFiles(OutputPath, "frame_*.bmp")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        foreach (var framePath in frames)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(framePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                FramePreviews.Add(bitmap);
            }
            catch
            {
                // One bad frame should not stop the rest from showing.
            }
        }
    }

    private void AppendProcessOutput(BackendRunResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            AppendSection("标准输出", result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            AppendSection("错误输出", result.StandardError.TrimEnd());
        }
    }

    private void AppendSection(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        _logBuilder.AppendLine();
        _logBuilder.AppendLine($"=== {title} ===");
        _logBuilder.AppendLine(content);
        LogText = _logBuilder.ToString();
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message.TrimEnd()}");
        LogText = _logBuilder.ToString();
    }

    private void AppendLiveSection(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        _liveLogBuilder.AppendLine();
        _liveLogBuilder.AppendLine($"=== {title} ===");
        _liveLogBuilder.AppendLine(content);
        LiveLogText = _liveLogBuilder.ToString();
    }

    private void AppendLiveLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _liveLogBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message.TrimEnd()}");
        LiveLogText = _liveLogBuilder.ToString();
    }

    private void AppendLiveLogThreadSafe(string message)
    {
        RunOnUiThread(() => AppendLiveLog(message));
    }

    private void OnPlaybackStatusChanged(object? sender, LivePlaybackStatusChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            PlaybackStatusText = e.Message;
            AppendPlaybackLog(e.Message);

            if (e.State is LivePlaybackState.Stopped or LivePlaybackState.Error)
            {
                IsPlaybackActive = false;
            }
        });
    }

    private void OnPlaybackLogReceived(object? sender, string message)
    {
        RunOnUiThread(() => AppendPlaybackLog(message));
    }

    private void AppendPlaybackLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _playbackLogBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message.TrimEnd()}");
        PlaybackLogText = _playbackLogBuilder.ToString();
    }

    private void OnLiveStreamExited(object? sender, int exitCode)
    {
        RunOnUiThread(() =>
        {
            IsLiveStreaming = false;
            LiveStatusText = exitCode == 0 ? "直播已停止" : $"FFmpeg 已退出：{exitCode}";
            AppendLiveLog($"FFmpeg 进程退出，退出码：{exitCode}。");
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
            return;
        }

        action();
    }

    private void RaiseCommandStates()
    {
        _chooseVideoCommand.RaiseCanExecuteChanged();
        _chooseOutputCommand.RaiseCanExecuteChanged();
        _openOutputCommand.RaiseCanExecuteChanged();
        _analyzeCommand.RaiseCanExecuteChanged();
        _extractFramesCommand.RaiseCanExecuteChanged();
        _extractAudioCommand.RaiseCanExecuteChanged();
        _runAllCommand.RaiseCanExecuteChanged();
        _startLiveCommand.RaiseCanExecuteChanged();
        _stopLiveCommand.RaiseCanExecuteChanged();
        _listLiveDevicesCommand.RaiseCanExecuteChanged();
        _startPlaybackCommand.RaiseCanExecuteChanged();
        _stopPlaybackCommand.RaiseCanExecuteChanged();
    }
}
