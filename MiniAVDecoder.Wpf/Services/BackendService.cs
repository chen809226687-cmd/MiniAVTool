using System.Diagnostics;
using System.IO;
using System.Text;
using MiniAVDecoder.Wpf.Models;

namespace MiniAVDecoder.Wpf.Services;

public sealed class BackendService : IBackendService
{
    public async Task<BackendRunResult> RunAsync(string command, string inputPath, string outputDir, int frameCount, CancellationToken cancellationToken = default)
    {
        var exePath = FindBackendExecutable();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new FileNotFoundException("Backend executable not found.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add(command);
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add(outputDir);
        if (frameCount > 0)
        {
            psi.ArgumentList.Add(frameCount.ToString());
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start backend process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        return new BackendRunResult(process.ExitCode, await stdoutTask, await stderrTask, exePath);
    }

    private static string? FindBackendExecutable()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            foreach (var candidate in BuildCandidates(current.FullName))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    private static IEnumerable<string> BuildCandidates(string root)
    {
        const string exeName = "MiniAVTool.Backend.exe";
        var configNames = new[] { "Debug", "Release" };
        var platforms = new[] { "x64", "Win32", string.Empty };

        foreach (var platform in platforms)
        {
            foreach (var config in configNames)
            {
                yield return string.IsNullOrEmpty(platform)
                    ? Path.Combine(root, "MiniAVTool.Backend", config, exeName)
                    : Path.Combine(root, "MiniAVTool.Backend", platform, config, exeName);
            }
        }
    }
}
