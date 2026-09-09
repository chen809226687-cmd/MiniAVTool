using System.Windows;
using MiniAVDecoder.Wpf.Services;
using MiniAVDecoder.Wpf.ViewModels;

namespace MiniAVDecoder.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 播放器对象由服务管理，VideoView 只负责把视频画面嵌入当前 Tab。
        var playbackService = new LibVlcPlaybackService();
        LiveVideoView.MediaPlayer = playbackService.MediaPlayer;

        DataContext = new MainViewModel(
            new BackendService(),
            new DialogService(),
            new LiveStreamingService(),
            playbackService);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 先从控件解绑播放器，再由 ViewModel 释放 LibVLC 原生资源。
        LiveVideoView.MediaPlayer = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
