using System.Diagnostics;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public interface IBackendService
{
    Task<BackendRunResult> RunAsync(string command, string inputPath, string outputDir, int frameCount, CancellationToken cancellationToken = default);
}
