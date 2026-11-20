using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

/// <summary>
/// 使用 WebView2 的 WebRTC 引擎接收 WHEP 流。SDP 信令由 C# 发送，
/// 媒体协商、DTLS/SRTP 解码和画面渲染由浏览器内核完成。
/// </summary>
public sealed class WebRtcPlaybackService : IRtcPlaybackService
{
    private readonly WebView2 _webView;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private readonly object _syncRoot = new();
    private Task? _initializationTask;
    private Uri? _currentWhepUri;
    private Uri? _sessionUri;
    private string _authorizationToken = string.Empty;
    private long _playbackGeneration;
    private bool _isActive;
    private bool _isPlaying;
    private bool _disposed;

    public WebRtcPlaybackService(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _webView.DefaultBackgroundColor = System.Drawing.Color.Black;
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    public event EventHandler<LivePlaybackStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<string>? LogReceived;

    public event EventHandler? FullscreenToggleRequested;

    public bool IsPlaying
    {
        get
        {
            lock (_syncRoot)
            {
                return _isPlaying;
            }
        }
    }

    public async Task PlayAsync(
        string whepUrl,
        string authorizationToken = "",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(whepUrl?.Trim(), UriKind.Absolute, out var whepUri)
            || (whepUri.Scheme != Uri.UriSchemeHttp && whepUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("RTC 播放需要有效的 WHEP http(s) 地址。", nameof(whepUrl));
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(true);
        var hasAudio = await EnsureSrsPublisherActiveAsync(
            whepUri,
            authorizationToken,
            cancellationToken).ConfigureAwait(true);
        await StopCoreAsync(raiseStopped: false, cancellationToken).ConfigureAwait(true);

        long generation;
        lock (_syncRoot)
        {
            _playbackGeneration++;
            generation = _playbackGeneration;
            _currentWhepUri = whepUri;
            _authorizationToken = authorizationToken?.Trim() ?? string.Empty;
            _isActive = true;
            _isPlaying = false;
        }

        RaiseLog($"开始建立 RTC (WHEP) 连接：{whepUri}");
        RaiseStatus(LivePlaybackState.Connecting, "正在建立 RTC 连接...");

        var scriptUrl = JsonSerializer.Serialize(whepUri.AbsoluteUri);
        var scriptHasAudio = hasAudio == false ? "false" : "true";
        await ExecuteScriptAsync(
            $"window.startWhep({scriptUrl}, {generation}, {scriptHasAudio});").ConfigureAwait(true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await StopCoreAsync(raiseStopped: true, cancellationToken).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _webView.WebMessageReceived -= OnWebMessageReceived;

        Uri? sessionUri;
        string token;
        lock (_syncRoot)
        {
            _playbackGeneration++;
            _isActive = false;
            _isPlaying = false;
            sessionUri = _sessionUri;
            token = _authorizationToken;
            _sessionUri = null;
        }

        if (_webView.CoreWebView2 != null)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("window.stopWhep();");
        }

        _ = DeleteSessionAsync(sessionUri, token);
    }

    private Task InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _initializationTask ??= InitializeCoreAsync();
            return _initializationTask.WaitAsync(cancellationToken);
        }
    }

    private async Task InitializeCoreAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiniAVDecoder.Wpf",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--autoplay-policy=no-user-gesture-required " +
                    "--disable-background-timer-throttling " +
                    "--disable-backgrounding-occluded-windows " +
                    "--disable-renderer-backgrounding " +
                    "--disable-features=CalculateNativeWinOcclusion"
            }).ConfigureAwait(true);

        await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        var core = _webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 初始化失败。");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsSwipeNavigationEnabled = false;

        var navigationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            if (args.IsSuccess)
            {
                navigationCompleted.TrySetResult(true);
            }
            else
            {
                navigationCompleted.TrySetException(
                    new InvalidOperationException($"RTC 播放页面加载失败：{args.WebErrorStatus}"));
            }
        }

        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.NavigateToString(RtcPlaybackPage);
        await navigationCompleted.Task.ConfigureAwait(true);
    }

    private async void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            switch (type)
            {
                case "offer":
                {
                    var sdp = root.GetProperty("sdp").GetString();
                    var generation = root.GetProperty("generation").GetInt64();
                    if (!string.IsNullOrWhiteSpace(sdp))
                    {
                        await HandleOfferAsync(sdp, generation).ConfigureAwait(true);
                    }

                    break;
                }

                case "status":
                    HandleBrowserStatus(
                        root.GetProperty("state").GetString(),
                        root.GetProperty("message").GetString());
                    break;

                case "toggleFullscreen":
                    FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetFailed($"RTC 页面消息处理失败：{ex.Message}");
        }
    }

    private async Task HandleOfferAsync(string offerSdp, long generation)
    {
        Uri? whepUri;
        string token;
        lock (_syncRoot)
        {
            if (_disposed || !_isActive || generation != _playbackGeneration)
            {
                return;
            }

            whepUri = _currentWhepUri;
            token = _authorizationToken;
        }

        if (whepUri == null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, whepUri)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(offerSdp))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sdp"));
            AddAuthorization(request, token);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(true);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"WHEP 握手失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。" +
                    (string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $" 服务器返回：{responseBody.Trim()}"));
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                throw new InvalidOperationException("WHEP 服务器没有返回 SDP answer。");
            }

            var sessionUri = ResolveLocation(whepUri, response.Headers.Location);
            lock (_syncRoot)
            {
                if (generation != _playbackGeneration || !_isActive)
                {
                    return;
                }

                _sessionUri = sessionUri;
            }

            var serializedAnswer = JsonSerializer.Serialize(responseBody);
            await ExecuteScriptAsync(
                $"window.applyWhepAnswer({serializedAnswer}, {generation});").ConfigureAwait(true);
            RaiseLog("WHEP SDP 协商完成，正在建立 DTLS/SRTP 媒体连接。");
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                if (generation != _playbackGeneration || !_isActive)
                {
                    return;
                }
            }

            SetFailed($"RTC 连接失败：{ex.Message}");
            RaiseLog("请确认 WHEP 地址有效，并检查媒体服务器 UDP 8000 和网络防火墙。");
        }
    }

    private async Task<bool?> EnsureSrsPublisherActiveAsync(
        Uri whepUri,
        string authorizationToken,
        CancellationToken cancellationToken)
    {
        if (!whepUri.AbsolutePath.Contains("/rtc/v1/whep", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var app = GetQueryParameter(whepUri, "app");
        var stream = GetQueryParameter(whepUri, "stream");
        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(stream))
        {
            return null;
        }

        var apiUri = new UriBuilder(whepUri)
        {
            Path = "/api/v1/streams",
            Query = string.Empty
        }.Uri;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUri);
            AddAuthorization(request, authorizationToken);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(true);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token)
                .ConfigureAwait(true);

            if (!document.RootElement.TryGetProperty("streams", out var streams)
                || streams.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in streams.EnumerateArray())
            {
                var itemApp = item.TryGetProperty("app", out var appValue)
                    ? appValue.GetString()
                    : null;
                var itemStream = item.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString()
                    : null;
                if (!string.Equals(itemApp, app, StringComparison.Ordinal)
                    || !string.Equals(itemStream, stream, StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.TryGetProperty("publish", out var publish)
                    && publish.TryGetProperty("active", out var active)
                    && active.ValueKind == JsonValueKind.True)
                {
                    var hasAudio = item.TryGetProperty("audio", out var audio)
                        && audio.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
                    RaiseLog($"SRS 流 {app}/{stream} 在线，音频轨道：{(hasAudio ? "有" : "无")}。");
                    return hasAudio;
                }

                break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // SRS API availability must not block other standards-compliant WHEP servers.
            return null;
        }
        catch
        {
            return null;
        }

        throw new InvalidOperationException(
            $"SRS 上 {app}/{stream} 当前没有在线推流。请先开始 RTC 推流，确认状态为“RTC 推流中”，再开始观看。");
    }

    private async Task StopCoreAsync(bool raiseStopped, CancellationToken cancellationToken)
    {
        bool wasActive;
        Uri? sessionUri;
        string token;
        lock (_syncRoot)
        {
            _playbackGeneration++;
            wasActive = _isActive;
            _isActive = false;
            _isPlaying = false;
            sessionUri = _sessionUri;
            token = _authorizationToken;
            _sessionUri = null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_initializationTask is { IsCompletedSuccessfully: true })
        {
            try
            {
                await ExecuteScriptAsync("window.stopWhep();").ConfigureAwait(true);
            }
            catch
            {
                // The native WebView may already be closing.
            }
        }

        await DeleteSessionAsync(sessionUri, token).ConfigureAwait(true);

        if (raiseStopped && wasActive)
        {
            RaiseLog("RTC 播放已停止。");
            RaiseStatus(LivePlaybackState.Stopped, "RTC 播放已停止");
        }
    }

    private void HandleBrowserStatus(string? state, string? message)
    {
        switch (state)
        {
            case "connected":
                RaiseStatus(LivePlaybackState.Buffering, "RTC 已连接，正在等待画面...");
                break;

            case "playing":
                lock (_syncRoot)
                {
                    _isPlaying = true;
                }

                RaiseStatus(LivePlaybackState.Playing, "RTC 直播播放中");
                break;

            case "buffering":
                RaiseStatus(LivePlaybackState.Buffering, message ?? "RTC 正在缓冲...");
                break;

            case "failed":
                SetFailed(message ?? "RTC 媒体连接失败。");
                break;

            case "closed":
            {
                bool wasActive;
                lock (_syncRoot)
                {
                    wasActive = _isActive;
                    _isActive = false;
                    _isPlaying = false;
                }

                if (wasActive)
                {
                    RaiseStatus(LivePlaybackState.Stopped, "RTC 直播已结束");
                }

                break;
            }
        }
    }

    private void SetFailed(string message)
    {
        Uri? sessionUri;
        string token;
        lock (_syncRoot)
        {
            _isActive = false;
            _isPlaying = false;
            sessionUri = _sessionUri;
            token = _authorizationToken;
            _sessionUri = null;
        }

        _ = DeleteSessionAsync(sessionUri, token);
        RaiseLog(message);
        RaiseStatus(LivePlaybackState.Error, message);
    }

    private Task ExecuteScriptAsync(string script)
    {
        var core = _webView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 尚未初始化。");
        return core.ExecuteScriptAsync(script);
    }

    private async Task DeleteSessionAsync(Uri? sessionUri, string token)
    {
        if (sessionUri == null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, sessionUri);
            AddAuthorization(request, token);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                RaiseLog($"WHEP 会话释放返回 HTTP {(int)response.StatusCode}。");
            }
        }
        catch
        {
            // The process may be exiting or the session may already have expired.
        }
    }

    private static Uri? ResolveLocation(Uri requestUri, Uri? location)
    {
        if (location == null)
        {
            return null;
        }

        return location.IsAbsoluteUri ? location : new Uri(requestUri, location);
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
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

    private static void AddAuthorization(HttpRequestMessage request, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        const string bearerPrefix = "Bearer ";
        var value = token.Trim();
        if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[bearerPrefix.Length..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }
    }

    private void RaiseStatus(LivePlaybackState state, string message)
    {
        StatusChanged?.Invoke(this, new LivePlaybackStatusChangedEventArgs(state, message));
    }

    private void RaiseLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }

    private const string RtcPlaybackPage = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <style>
            html, body {
              width: 100%;
              height: 100%;
              margin: 0;
              overflow: hidden;
              background: #101216;
            }
            video {
              display: block;
              width: 100%;
              height: 100%;
              object-fit: contain;
              background: #101216;
            }
          </style>
        </head>
        <body>
          <video id="remoteVideo" autoplay playsinline></video>
          <script>
            const video = document.getElementById('remoteVideo');
            let peerConnection = null;
            let activeGeneration = 0;
            let lastStatusKey = '';
            let receivedStream = null;
            let frameMonitorTimer = null;
            let lastVideoFrameCount = -1;
            let lastFrameProgressAt = 0;
            let lastRebindAt = 0;

            function post(message) {
              window.chrome.webview.postMessage(message);
            }

            function postStatus(state, message) {
              const statusKey = state + '|' + (message || '');
              if (statusKey === lastStatusKey) {
                return;
              }

              lastStatusKey = statusKey;
              post({ type: 'status', state: state, message: message || '' });
            }

            async function waitForIceGathering(connection) {
              if (connection.iceGatheringState === 'complete') {
                return;
              }

              await new Promise(resolve => {
                const timer = setTimeout(resolve, 2500);
                connection.addEventListener('icegatheringstatechange', () => {
                  if (connection.iceGatheringState === 'complete') {
                    clearTimeout(timer);
                    resolve();
                  }
                });
              });
            }

            function stopFrameMonitor() {
              if (frameMonitorTimer) {
                clearInterval(frameMonitorTimer);
                frameMonitorTimer = null;
              }
            }

            function startFrameMonitor(generation) {
              stopFrameMonitor();
              lastVideoFrameCount = -1;
              lastFrameProgressAt = Date.now();
              lastRebindAt = 0;

              frameMonitorTimer = setInterval(() => {
                if (generation !== activeGeneration || !peerConnection || !receivedStream) {
                  return;
                }

                const quality = video.getVideoPlaybackQuality
                  ? video.getVideoPlaybackQuality()
                  : null;
                const frameCount = quality
                  ? quality.totalVideoFrames
                  : (video.webkitDecodedFrameCount || 0);
                const now = Date.now();

                if (frameCount > lastVideoFrameCount) {
                  lastVideoFrameCount = frameCount;
                  lastFrameProgressAt = now;
                  return;
                }

                if (peerConnection.connectionState !== 'connected'
                    || now - lastFrameProgressAt < 4000
                    || now - lastRebindAt < 5000) {
                  return;
                }

                // Some WebView2/GPU combinations keep rendering only the first
                // decoded frame. Reattaching the same MediaStream restarts the
                // compositor without rebuilding the RTC session.
                lastRebindAt = now;
                lastFrameProgressAt = now;
                const stream = receivedStream;
                postStatus('buffering', '检测到 RTC 画面停止刷新，正在恢复显示...');
                video.srcObject = null;
                setTimeout(() => {
                  if (generation !== activeGeneration) {
                    return;
                  }

                  video.srcObject = stream;
                  video.play().catch(() => {});
                }, 80);
              }, 1000);
            }

            window.startWhep = async function (whepUrl, generation, hasAudio) {
              window.stopWhep(false);
              activeGeneration = generation;
              lastStatusKey = '';
              receivedStream = null;
              postStatus('connecting', '正在创建 WebRTC 播放连接...');

              try {
                const connection = new RTCPeerConnection();
                peerConnection = connection;

                if (hasAudio) {
                  connection.addTransceiver('audio', { direction: 'recvonly' });
                }

                // SRS omits the audio m-line for video-only streams. The offer
                // must contain exactly the tracks present in the SDP answer.
                connection.addTransceiver('video', { direction: 'recvonly' });

                connection.ontrack = event => {
                  if (generation !== activeGeneration) {
                    return;
                  }

                  const stream = event.streams && event.streams.length > 0
                    ? event.streams[0]
                    : new MediaStream([event.track]);
                  if (receivedStream !== stream) {
                    receivedStream = stream;
                    video.srcObject = receivedStream;
                    video.muted = !hasAudio;
                    startFrameMonitor(generation);
                    video.play().catch(error => {
                      if (generation === activeGeneration) {
                        postStatus('failed', 'RTC 视频自动播放失败：' + error.message);
                      }
                    });
                  }
                };

                connection.onconnectionstatechange = () => {
                  if (generation !== activeGeneration) {
                    return;
                  }

                  switch (connection.connectionState) {
                    case 'connected':
                      postStatus('connected', 'RTC 媒体连接已建立');
                      break;
                    case 'disconnected':
                      postStatus('buffering', 'RTC 媒体连接暂时中断，正在恢复...');
                      break;
                    case 'failed':
                      postStatus('failed', 'RTC 媒体连接失败：请确认推流仍在运行，并检查 UDP 8000 和防火墙。');
                      break;
                    case 'closed':
                      postStatus('closed', 'RTC 媒体连接已关闭');
                      break;
                  }
                };

                video.onplaying = () => {
                  if (generation === activeGeneration) {
                    postStatus('playing', 'RTC 直播播放中');
                  }
                };

                const offer = await connection.createOffer();
                await connection.setLocalDescription(offer);
                await waitForIceGathering(connection);

                if (generation !== activeGeneration || !connection.localDescription) {
                  return;
                }

                post({
                  type: 'offer',
                  sdp: connection.localDescription.sdp,
                  generation: generation
                });
              } catch (error) {
                if (generation === activeGeneration) {
                  postStatus('failed', 'RTC 初始化失败：' + error.message);
                }
              }
            };

            window.applyWhepAnswer = async function (answerSdp, generation) {
              if (generation !== activeGeneration || !peerConnection) {
                return;
              }

              try {
                await peerConnection.setRemoteDescription({
                  type: 'answer',
                  sdp: answerSdp
                });
                postStatus('buffering', 'WHEP 协商完成，正在接收 RTC 媒体...');
              } catch (error) {
                if (generation === activeGeneration) {
                  postStatus('failed', '应用 WHEP answer 失败：' + error.message);
                }
              }
            };

            window.stopWhep = function (notify = true) {
              activeGeneration = 0;
              stopFrameMonitor();
              receivedStream = null;
              video.pause();
              video.srcObject = null;

              if (peerConnection) {
                peerConnection.ontrack = null;
                peerConnection.onconnectionstatechange = null;
                peerConnection.close();
                peerConnection = null;
              }

              if (notify) {
                postStatus('closed', 'RTC 播放已停止');
              }
            };

            video.addEventListener('dblclick', () => {
              post({ type: 'toggleFullscreen' });
            });

            document.addEventListener('keydown', event => {
              if (event.key === 'Escape') {
                post({ type: 'toggleFullscreen' });
              }
            });
          </script>
        </body>
        </html>
        """;
}
