namespace MiniAVDecoder.Wpf.Models;

public sealed record BackendRunResult(int ExitCode, string StandardOutput, string StandardError, string ExecutablePath)
{
    public bool Success => ExitCode == 0;
}
