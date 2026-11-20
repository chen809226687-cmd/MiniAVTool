using MiniAVDecoder.Wpf.Infrastructure;
using MiniAVDecoder.Wpf.Models;
using MiniAVDecoder.Wpf.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniAVDecoder.Wpf.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IBackendService _backendService;
    private readonly IDialogService _dialogService;
    private readonly ILiveStreamingService _liveStreamingService;
    private readonly ILivePlaybackService _livePlaybackService;
    private readonly IRtcPlaybackService? _rtcPlaybackService;
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

    private LiveStreamProtocol _streamProtocol = LiveStreamProtocol.Rtmp;
    private string _rtmpUrl = "rtmp://111.229.145.58/live/test";
    private string _rtcUrl = "http://111.229.145.58:1985/rtc/v1/whip/?app=live&stream=test";
    private string _rtcAuthorizationToken = string.Empty;
    private LiveVideoSourceMode _videoSourceMode = LiveVideoSourceMode.SharedVideo;
    private string _cameraDeviceName = string.Empty;
    private bool _includeAudio;
    private string _audioDeviceName = string.Empty;
    private int _liveFrameRate = 30;
    private string _liveVideoSize = "1280x720";
    private int _liveVideoBitrateKbps = 2500;
    private string _liveScaleMode = "原始比例";
    private string _liveStatusText = "直播未开始";
    private string _liveLogText = string.Empty;
    private bool _isLiveStreaming;
    private LiveStreamProtocol? _activeStreamProtocol;
    private bool _rtcTransportFailureLogged;
    private LivePlaybackMode _playbackMode = LivePlaybackMode.Rtmp;
    private string _rtmpPlaybackUrl = "rtmp://111.229.145.58/live/test";
    private string _rtcPlaybackUrl = "http://111.229.145.58:1985/rtc/v1/whep/?app=live&stream=test";
    private string _rtcPlaybackAuthorizationToken = string.Empty;
    private string _playbackStatusText = "未播放";
    private string _playbackLogText = string.Empty;
    private bool _isPlaybackActive;
    private bool _hasPlaybackStarted;
    private LivePlaybackMode? _activePlaybackMode;

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
        ILivePlaybackService livePlaybackService,
        IRtcPlaybackService? rtcPlaybackService = null)
    {
        _backendService = backendService;
        _dialogService = dialogService;
        _liveStreamingService = liveStreamingService;
        _livePlaybackService = livePlaybackService;
        _rtcPlaybackService = rtcPlaybackService;
        _liveStreamingService.StreamExited += OnLiveStreamExited;
        _livePlaybackService.StatusChanged += OnPlaybackStatusChanged;
        _livePlaybackService.LogReceived += OnPlaybackLogReceived;
        if (_rtcPlaybackService != null)
        {
            _rtcPlaybackService.StatusChanged += OnPlaybackStatusChanged;
            _rtcPlaybackService.LogReceived += OnPlaybackLogReceived;
        }

        _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniAVDecoderOutput");
        FramePreviews = new ObservableCollection<ImageSource>();
        CameraDevices = new ObservableCollection<string>();

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
        AppendLiveLog("直播工具已就绪，可在 RTMP 与 RTC (SRS bridge) 之间切换。");
        AppendLiveLog("RTMP 使用 rtmp:// 地址；RTC 使用媒体服务器的 http(s):// WHIP 地址。");
        AppendLiveLog("RTC 推流经 SRS 的 RTMP 接入自动转换为 WebRTC；RTC 观看仍需 UDP 8000。");
        AppendLiveLog("程序将使用项目自带的 FFmpeg：Tools\\ffmpeg\\ffmpeg.exe。");
        AppendPlaybackLog("观看直播功能已就绪。");
        AppendPlaybackLog("RTMP 使用 LibVLC 播放；RTC 使用 WebView2 和 WHEP 播放。");
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

    public LiveStreamProtocol StreamProtocol
    {
        get => _streamProtocol;
        set
        {
            if (SetProperty(ref _streamProtocol, value))
            {
                RaisePropertyChanged(nameof(IsRtmpProtocol));
                RaisePropertyChanged(nameof(IsRtcProtocol));
                RaisePropertyChanged(nameof(StreamUrl));
                RaisePropertyChanged(nameof(StreamAddressLabel));
                RaisePropertyChanged(nameof(StreamAddressHint));
                RaiseCommandStates();
            }
        }
    }

    public bool IsRtmpProtocol
    {
        get => StreamProtocol == LiveStreamProtocol.Rtmp;
        set
        {
            if (value)
            {
                StreamProtocol = LiveStreamProtocol.Rtmp;
            }
        }
    }

    public bool IsRtcProtocol
    {
        get => StreamProtocol == LiveStreamProtocol.Rtc;
        set
        {
            if (value)
            {
                StreamProtocol = LiveStreamProtocol.Rtc;
            }
        }
    }

    public string StreamUrl
    {
        get => StreamProtocol == LiveStreamProtocol.Rtc ? _rtcUrl : _rtmpUrl;
        set
        {
            var currentValue = StreamUrl;
            if (string.Equals(currentValue, value, StringComparison.Ordinal))
            {
                return;
            }

            if (StreamProtocol == LiveStreamProtocol.Rtc)
            {
                _rtcUrl = value;
            }
            else
            {
                _rtmpUrl = value;
            }

            RaisePropertyChanged(nameof(StreamUrl));
            RaiseCommandStates();
        }
    }

    public string RtcAuthorizationToken
    {
        get => _rtcAuthorizationToken;
        set => SetProperty(ref _rtcAuthorizationToken, value);
    }

    public string StreamAddressLabel =>
        StreamProtocol == LiveStreamProtocol.Rtc ? "RTC WHIP 地址" : "RTMP 推流地址";

    public string StreamAddressHint => StreamProtocol == LiveStreamProtocol.Rtc
        ? "例如 http://127.0.0.1:1985/rtc/v1/whip/?app=live&stream=test"
        : "例如 rtmp://127.0.0.1/live/test";

    public LiveVideoSourceMode VideoSourceMode
    {
        get => _videoSourceMode;
        set
        {
            if (SetProperty(ref _videoSourceMode, value))
            {
                RaisePropertyChanged(nameof(IsCameraMode));
                RaisePropertyChanged(nameof(IsSharedVideoMode));
                RaiseCommandStates();
            }
        }
    }

    public bool IsCameraMode
    {
        get => VideoSourceMode == LiveVideoSourceMode.Camera;
        set
        {
            if (value)
            {
                VideoSourceMode = LiveVideoSourceMode.Camera;
            }
        }
    }

    public bool IsSharedVideoMode
    {
        get => VideoSourceMode == LiveVideoSourceMode.SharedVideo;
        set
        {
            if (value)
            {
                VideoSourceMode = LiveVideoSourceMode.SharedVideo;
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

    public ObservableCollection<string> CameraDevices { get; }

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

    public IReadOnlyList<string> LiveVideoSizeOptions { get; } =
    [
        "640x360",
        "854x480",
        "1280x720",
        "1920x1080",
        "2560x1440"
    ];

    public IReadOnlyList<int> LiveFrameRateOptions { get; } =
    [
        15,
        24,
        30,
        60
    ];

    public IReadOnlyList<int> LiveVideoBitrateOptions { get; } =
    [
        800,
        1500,
        2500,
        4500,
        6000
    ];

    public IReadOnlyList<string> LiveScaleModeOptions { get; } =
    [
        "原始比例",
        "自动缩放"
    ];

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

    public string LiveScaleMode
    {
        get => _liveScaleMode;
        set => SetProperty(ref _liveScaleMode, value);
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

    public LivePlaybackMode PlaybackMode
    {
        get => _playbackMode;
        set
        {
            if (SetProperty(ref _playbackMode, value))
            {
                RaisePropertyChanged(nameof(IsRtmpPlayback));
                RaisePropertyChanged(nameof(IsRtcPlayback));
                RaisePropertyChanged(nameof(PlaybackUrl));
                RaisePropertyChanged(nameof(PlaybackUrlLabel));
                RaisePropertyChanged(nameof(PlaybackUrlHint));
                RaiseCommandStates();
            }
        }
    }

    public bool IsRtmpPlayback
    {
        get => PlaybackMode == LivePlaybackMode.Rtmp;
        set
        {
            if (value)
            {
                PlaybackMode = LivePlaybackMode.Rtmp;
            }
        }
    }

    public bool IsRtcPlayback
    {
        get => PlaybackMode == LivePlaybackMode.Rtc;
        set
        {
            if (value)
            {
                PlaybackMode = LivePlaybackMode.Rtc;
            }
        }
    }

    public bool CanChangePlaybackProtocol => !IsPlaybackActive;

    public string PlaybackUrl
    {
        get => PlaybackMode == LivePlaybackMode.Rtc ? _rtcPlaybackUrl : _rtmpPlaybackUrl;
        set
        {
            if (string.Equals(PlaybackUrl, value, StringComparison.Ordinal))
            {
                return;
            }

            if (PlaybackMode == LivePlaybackMode.Rtc)
            {
                _rtcPlaybackUrl = value;
            }
            else
            {
                _rtmpPlaybackUrl = value;
            }

            RaisePropertyChanged(nameof(PlaybackUrl));
            RaiseCommandStates();
        }
    }

    public string RtcPlaybackAuthorizationToken
    {
        get => _rtcPlaybackAuthorizationToken;
        set => SetProperty(ref _rtcPlaybackAuthorizationToken, value);
    }

    public string PlaybackUrlLabel =>
        PlaybackMode == LivePlaybackMode.Rtc ? "WHEP 地址" : "播放地址";

    public string PlaybackUrlHint => PlaybackMode == LivePlaybackMode.Rtc
        ? "例如 http://127.0.0.1:1985/rtc/v1/whep/?app=live&stream=test"
        : "支持 rtmp://、http://、https:// 直播地址";

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
                RaisePropertyChanged(nameof(CanChangePlaybackProtocol));
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
        if (_rtcPlaybackService != null)
        {
            _rtcPlaybackService.StatusChanged -= OnPlaybackStatusChanged;
            _rtcPlaybackService.LogReceived -= OnPlaybackLogReceived;
            _rtcPlaybackService.Dispose();
        }

        _liveStreamingService.Dispose();
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
    private bool CanStartLive() => !IsLiveStreaming
        && !string.IsNullOrWhiteSpace(StreamUrl)
        && (!IsCameraMode || !string.IsNullOrWhiteSpace(CameraDeviceName));
    private bool CanStopLive() => IsLiveStreaming;
    private bool CanListLiveDevices() => !IsLiveStreaming;
    private bool CanStartPlayback() =>
        !IsPlaybackActive && IsSupportedPlaybackUrl(PlaybackUrl, PlaybackMode);
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
            _activeStreamProtocol = StreamProtocol;
            _rtcTransportFailureLogged = false;
            IsLiveStreaming = true;
            var protocolName = GetProtocolDisplayName();
            LiveStatusText = $"正在启动 {protocolName} 推流...";
            await _liveStreamingService.StartAsync(BuildLiveOptions(), AppendLiveLogThreadSafe);
            LiveStatusText = $"{protocolName} 推流中";
        }
        catch (Exception ex)
        {
            _activeStreamProtocol = null;
            IsLiveStreaming = false;
            LiveStatusText = "推流启动失败";
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
            _activeStreamProtocol = null;
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
            var videoDevices = DirectShowDeviceParser.ParseVideoDeviceNames(output);

            CameraDevices.Clear();
            foreach (var device in videoDevices)
            {
                CameraDevices.Add(device);
            }

            if (videoDevices.Count > 0
                && (string.IsNullOrWhiteSpace(CameraDeviceName)
                    || !videoDevices.Contains(CameraDeviceName, StringComparer.OrdinalIgnoreCase)))
            {
                CameraDeviceName = videoDevices[0];
            }

            AppendLiveSection("FFmpeg DirectShow 设备列表", output.TrimEnd());
            LiveStatusText = videoDevices.Count == 0
                ? "未找到摄像头设备"
                : $"已找到 {videoDevices.Count} 个摄像头设备";
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
            await StopPlaybackEnginesAsync();
            _activePlaybackMode = PlaybackMode;
            IsPlaybackActive = true;
            _hasPlaybackStarted = false;
            PlaybackStatusText = PlaybackMode == LivePlaybackMode.Rtc
                ? "正在建立 RTC 连接..."
                : "正在连接...";

            if (PlaybackMode == LivePlaybackMode.Rtc)
            {
                if (_rtcPlaybackService == null)
                {
                    throw new InvalidOperationException("RTC 播放服务尚未初始化。");
                }

                await _rtcPlaybackService.PlayAsync(
                    PlaybackUrl,
                    RtcPlaybackAuthorizationToken);
            }
            else
            {
                await _livePlaybackService.PlayAsync(PlaybackUrl);
            }
        }
        catch (Exception ex)
        {
            _activePlaybackMode = null;
            IsPlaybackActive = false;
            _hasPlaybackStarted = false;
            PlaybackStatusText = "播放启动失败";
            AppendPlaybackLog(ex.Message);
        }
    }

    private async Task StopPlaybackAsync()
    {
        try
        {
            PlaybackStatusText = "正在停止...";
            await StopPlaybackEnginesAsync();
            IsPlaybackActive = false;
            _activePlaybackMode = null;
            _hasPlaybackStarted = false;
            PlaybackStatusText = "已停止";
            AppendPlaybackLog("已停止播放。");
        }
        catch (Exception ex)
        {
            PlaybackStatusText = "停止失败";
            AppendPlaybackLog(ex.Message);
        }
    }

    private async Task StopPlaybackEnginesAsync()
    {
        await _livePlaybackService.StopAsync();
        if (_rtcPlaybackService != null)
        {
            await _rtcPlaybackService.StopAsync();
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
        if (!IsSupportedPushUrl(StreamUrl, StreamProtocol))
        {
            AppendLiveLog(StreamProtocol == LiveStreamProtocol.Rtc
                ? "请输入 RTC (WHIP) 地址，例如 http://127.0.0.1:1985/rtc/v1/whip/?app=live&stream=test。"
                : "请输入 RTMP 推流地址，例如 rtmp://127.0.0.1/live/test。");
            return false;
        }

        if (IsCameraMode && string.IsNullOrWhiteSpace(CameraDeviceName))
        {
            AppendLiveLog("摄像头模式需要先刷新并选择一个摄像头设备。");
            return false;
        }

        if (IncludeAudio && string.IsNullOrWhiteSpace(AudioDeviceName))
        {
            AppendLiveLog("已勾选音频但未填写麦克风设备名，将只推送视频。");
            IncludeAudio = false;
        }

        return true;
    }

    private static bool IsSupportedPushUrl(string? url, LiveStreamProtocol protocol)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return protocol == LiveStreamProtocol.Rtc
            ? uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            : uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("rtmps", StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidatePlaybackSettings()
    {
        if (!IsSupportedPlaybackUrl(PlaybackUrl, PlaybackMode))
        {
            AppendPlaybackLog(PlaybackMode == LivePlaybackMode.Rtc
                ? "请输入有效的 WHEP 地址，例如 http://111.229.145.58:1985/rtc/v1/whep/?app=live&stream=test。"
                : "请输入有效的直播地址，例如 rtmp://111.229.145.58/live/test。");
            return false;
        }

        return true;
    }

    private static bool IsSupportedPlaybackUrl(string? url, LivePlaybackMode mode)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return mode == LivePlaybackMode.Rtc
            ? uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            : uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
    }

    private LiveStreamOptions BuildLiveOptions()
    {
        return new LiveStreamOptions
        {
            Protocol = StreamProtocol,
            PushUrl = StreamUrl,
            AuthorizationToken = StreamProtocol == LiveStreamProtocol.Rtc
                ? RtcAuthorizationToken
                : string.Empty,
            VideoSourceMode = VideoSourceMode,
            CameraDeviceName = CameraDeviceName,
            IncludeAudio = IncludeAudio,
            AudioDeviceName = AudioDeviceName,
            FrameRate = LiveFrameRate,
            VideoSize = LiveVideoSize,
            VideoBitrateKbps = LiveVideoBitrateKbps
        };
    }

    private string GetProtocolDisplayName()
    {
        if (StreamProtocol != LiveStreamProtocol.Rtc)
        {
            return "RTMP";
        }

        return StreamUrl.Contains("/rtc/v1/whip", StringComparison.OrdinalIgnoreCase)
            ? "RTC (SRS bridge)"
            : "RTC (WHIP)";
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

        if (_activeStreamProtocol == LiveStreamProtocol.Rtc
            && !_rtcTransportFailureLogged
            && message.Contains("DTLS handshake timeout", StringComparison.OrdinalIgnoreCase))
        {
            _rtcTransportFailureLogged = true;
            _liveLogBuilder.AppendLine();
            _liveLogBuilder.AppendLine("=== RTC 连接诊断 ===");
            _liveLogBuilder.AppendLine(
                "WHIP 建流接口可访问，但 WebRTC 媒体握手超时。请确认媒体服务器已放行 UDP 8000，" +
                "Docker 运行时包含 -p 8000:8000/udp（Compose 中为 \"8000:8000/udp\"），" +
                "云服务器安全组也允许入站 UDP 8000，修改后重启 SRS 容器。");
            LiveStatusText = "RTC 握手失败：请放行 UDP 8000";
        }

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
            AppendPlaybackLog(e.Message);

            switch (e.State)
            {
                case LivePlaybackState.Playing:
                    _hasPlaybackStarted = true;
                    IsPlaybackActive = true;
                    PlaybackStatusText = e.Message;
                    break;

                case LivePlaybackState.Connecting:
                case LivePlaybackState.Buffering:
                case LivePlaybackState.Paused:
                    IsPlaybackActive = true;
                    if (!_hasPlaybackStarted && !IsActivePlaybackEnginePlaying())
                    {
                        PlaybackStatusText = e.Message;
                    }

                    break;

                case LivePlaybackState.Stopped:
                case LivePlaybackState.Error:
                    _hasPlaybackStarted = false;
                    IsPlaybackActive = false;
                    _activePlaybackMode = null;
                    PlaybackStatusText = e.Message;
                    break;
            }
        });
    }

    private bool IsActivePlaybackEnginePlaying()
    {
        return _activePlaybackMode == LivePlaybackMode.Rtc
            ? _rtcPlaybackService?.IsPlaying == true
            : _livePlaybackService.IsPlaying;
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
            var failedProtocol = _activeStreamProtocol;
            IsLiveStreaming = false;
            LiveStatusText = exitCode == 0
                ? "推流已停止"
                : failedProtocol == LiveStreamProtocol.Rtc
                    && (_rtcTransportFailureLogged || exitCode == -138)
                    ? "RTC 握手失败：请放行 UDP 8000"
                    : $"FFmpeg 已退出：{exitCode}";
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
