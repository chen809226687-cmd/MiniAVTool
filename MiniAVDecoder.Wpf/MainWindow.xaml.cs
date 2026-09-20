using MiniAVDecoder.Wpf.Services;
using MiniAVDecoder.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MiniAVDecoder.Wpf;

public partial class MainWindow : Window
{
    private readonly ILivePlaybackService _playbackService;
    private WindowState _normalWindowState;
    private WindowStyle _normalWindowStyle;
    private ResizeMode _normalResizeMode;
    private Thickness _normalRootMargin;
    private Thickness _normalPlaybackPageMargin;
    private GridLength _normalTitleRowHeight;
    private GridLength _normalPlaybackControlRowHeight;
    private GridLength _normalPlaybackSpacerRowHeight;
    private GridLength _normalPlaybackLogRowHeight;
    private ControlTemplate? _normalTabControlTemplate;
    private bool _normalTopmost;
    private bool _isFullscreen;
    private bool _isDisposed;

    public MainWindow()
    {
        InitializeComponent();

        // 播放器对象由服务管理，VideoView 只负责把视频画面嵌入当前 Tab。
        _playbackService = new LibVlcPlaybackService();
        LiveVideoView.MediaPlayer = _playbackService.MediaPlayer;

        DataContext = new MainViewModel(
            new BackendService(),
            new DialogService(),
            new LiveStreamingService(),
            _playbackService);
    }

    private void OnToggleFullscreenButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
            return;
        }

        EnterFullscreen();
    }

    private void OnPlaybackVideoMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (_isFullscreen)
            {
                ExitFullscreen();
            }
            else
            {
                EnterFullscreen();
            }

            e.Handled = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isFullscreen && e.Key == System.Windows.Input.Key.Escape)
        {
            ExitFullscreen();
            e.Handled = true;
        }
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen)
        {
            return;
        }

        _isFullscreen = true;
        SaveNormalLayout();

        // 不切换 VideoView 或 MediaPlayer，只把当前窗口变成全屏。
        // LibVLC 的 WPF 渲染绑定在原生窗口句柄上，跨窗口搬播放器容易黑屏。
        MainRoot.Margin = new Thickness(0);
        PlaybackPageGrid.Margin = new Thickness(0);
        AppHeaderPanel.Visibility = Visibility.Collapsed;
        PlaybackControlPanel.Visibility = Visibility.Collapsed;
        PlaybackLogPanel.Visibility = Visibility.Collapsed;

        TitleRow.Height = new GridLength(0);
        PlaybackControlRow.Height = new GridLength(0);
        PlaybackSpacerRow.Height = new GridLength(0);
        PlaybackLogRow.Height = new GridLength(0);

        PlaybackVideoBorder.BorderThickness = new Thickness(0);
        PlaybackVideoBorder.CornerRadius = new CornerRadius(0);
        MainTabControl.Template = CreateContentOnlyTabTemplate();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Maximized;
        Activate();
        Focus();
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen)
        {
            return;
        }

        _isFullscreen = false;

        WindowState = _normalWindowState;
        WindowStyle = _normalWindowStyle;
        ResizeMode = _normalResizeMode;
        Topmost = _normalTopmost;

        MainRoot.Margin = _normalRootMargin;
        PlaybackPageGrid.Margin = _normalPlaybackPageMargin;
        TitleRow.Height = _normalTitleRowHeight;
        PlaybackControlRow.Height = _normalPlaybackControlRowHeight;
        PlaybackSpacerRow.Height = _normalPlaybackSpacerRowHeight;
        PlaybackLogRow.Height = _normalPlaybackLogRowHeight;
        MainTabControl.Template = _normalTabControlTemplate;

        AppHeaderPanel.Visibility = Visibility.Visible;
        PlaybackControlPanel.Visibility = Visibility.Visible;
        PlaybackLogPanel.Visibility = Visibility.Visible;
        PlaybackVideoBorder.BorderThickness = new Thickness(1);
        PlaybackVideoBorder.CornerRadius = new CornerRadius(6);
    }

    private void SaveNormalLayout()
    {
        _normalWindowState = WindowState;
        _normalWindowStyle = WindowStyle;
        _normalResizeMode = ResizeMode;
        _normalTopmost = Topmost;
        _normalRootMargin = MainRoot.Margin;
        _normalPlaybackPageMargin = PlaybackPageGrid.Margin;
        _normalTitleRowHeight = TitleRow.Height;
        _normalPlaybackControlRowHeight = PlaybackControlRow.Height;
        _normalPlaybackSpacerRowHeight = PlaybackSpacerRow.Height;
        _normalPlaybackLogRowHeight = PlaybackLogRow.Height;
        _normalTabControlTemplate = MainTabControl.Template;
    }

    private static ControlTemplate CreateContentOnlyTabTemplate()
    {
        var template = new ControlTemplate(typeof(System.Windows.Controls.TabControl));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        template.VisualTree = contentPresenter;
        return template;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_isFullscreen)
        {
            ExitFullscreen();
        }

        DisposeViewModel();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isDisposed)
        {
            base.OnClosed(e);
            return;
        }

        if (_isFullscreen)
        {
            ExitFullscreen();
        }

        // 先从控件解绑播放器，再由 ViewModel 释放 LibVLC 原生资源。
        LiveVideoView.MediaPlayer = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private void DisposeViewModel()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        LiveVideoView.MediaPlayer = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }
}
