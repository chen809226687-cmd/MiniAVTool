using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniAVDecoder.Wpf.Infrastructure;
using MiniAVDecoder.Wpf.Models;
using MiniAVDecoder.Wpf.Services;

namespace MiniAVDecoder.Wpf.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IBackendService _backendService;
    private readonly IDialogService _dialogService;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly StringBuilder _logBuilder = new();
    private string _inputPath = string.Empty;
    private string _outputPath = string.Empty;
    private string _statusText = "Ready";
    private string _logText = string.Empty;
    private int _frameCount = 30;
    private bool _isBusy;

    private readonly RelayCommand _chooseVideoCommand;
    private readonly RelayCommand _chooseOutputCommand;
    private readonly RelayCommand _openOutputCommand;
    private readonly AsyncRelayCommand _analyzeCommand;
    private readonly AsyncRelayCommand _extractFramesCommand;
    private readonly AsyncRelayCommand _extractAudioCommand;
    private readonly AsyncRelayCommand _runAllCommand;

    public MainViewModel()
        : this(new BackendService(), new DialogService())
    {
    }

    public MainViewModel(IBackendService backendService, IDialogService dialogService)
    {
        _backendService = backendService;
        _dialogService = dialogService;

        _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniAVDecoderOutput");
        FramePreviews = new ObservableCollection<ImageSource>();

        _chooseVideoCommand = new RelayCommand(ChooseVideo, CanUseDialogs);
        _chooseOutputCommand = new RelayCommand(ChooseOutput, CanUseDialogs);
        _openOutputCommand = new RelayCommand(OpenOutput, CanOpenOutput);
        _analyzeCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("analyze", refreshFrames: false), CanRunBackend);
        _extractFramesCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("extract-frames", refreshFrames: true), CanRunBackend);
        _extractAudioCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("extract-audio", refreshFrames: false), CanRunBackend);
        _runAllCommand = new AsyncRelayCommand(() => ExecuteBackendAsync("all", refreshFrames: true), CanRunBackend);

        AppendLog("Tool is ready.");
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

    public ObservableCollection<ImageSource> FramePreviews { get; }

    public RelayCommand ChooseVideoCommand => _chooseVideoCommand;
    public RelayCommand ChooseOutputCommand => _chooseOutputCommand;
    public RelayCommand OpenOutputCommand => _openOutputCommand;
    public AsyncRelayCommand AnalyzeCommand => _analyzeCommand;
    public AsyncRelayCommand ExtractFramesCommand => _extractFramesCommand;
    public AsyncRelayCommand ExtractAudioCommand => _extractAudioCommand;
    public AsyncRelayCommand RunAllCommand => _runAllCommand;

    private void ChooseVideo()
    {
        var file = _dialogService.PickVideoFile();
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        InputPath = file;
        AppendLog($"Selected file: {file}");
    }

    private void ChooseOutput()
    {
        var folder = _dialogService.PickFolder(OutputPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        OutputPath = folder;
        AppendLog($"Output folder: {folder}");
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
    private bool CanRunBackend() => !IsBusy && File.Exists(InputPath);

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
                StatusText = $"Backend exit code: {result.ExitCode}";
                AppendLog($"Backend failed: {result.ExecutablePath}");
                return;
            }

            StatusText = $"Completed: {command}";
            await LoadMediaInfoAsync();

            if (refreshFrames)
            {
                RefreshFramePreviews();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Execution failed";
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(InputPath) || !File.Exists(InputPath))
        {
            AppendLog("Please select a valid video file first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MiniAVDecoderOutput");
        }

        Directory.CreateDirectory(OutputPath);
        return true;
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

            AppendSection("Media info", MediaReportBuilder.Build(info, json));
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to read media_info.json: {ex.Message}");
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
            AppendSection("stdout", result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            AppendSection("stderr", result.StandardError.TrimEnd());
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

    private void RaiseCommandStates()
    {
        _chooseVideoCommand.RaiseCanExecuteChanged();
        _chooseOutputCommand.RaiseCanExecuteChanged();
        _openOutputCommand.RaiseCanExecuteChanged();
        _analyzeCommand.RaiseCanExecuteChanged();
        _extractFramesCommand.RaiseCanExecuteChanged();
        _extractAudioCommand.RaiseCanExecuteChanged();
        _runAllCommand.RaiseCanExecuteChanged();
    }
}
