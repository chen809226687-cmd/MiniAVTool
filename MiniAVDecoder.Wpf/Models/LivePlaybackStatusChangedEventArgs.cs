namespace MiniAVDecoder.Wpf.Models;

public enum LivePlaybackState
{
    Connecting,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Error
}

public sealed class LivePlaybackStatusChangedEventArgs : EventArgs
{
    public LivePlaybackStatusChangedEventArgs(LivePlaybackState state, string message)
    {
        State = state;
        Message = message;
    }

    public LivePlaybackState State { get; }

    public string Message { get; }
}
