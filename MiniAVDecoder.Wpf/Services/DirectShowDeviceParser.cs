using System.Text.RegularExpressions;

namespace MiniAVDecoder.Wpf.Services;

internal static partial class DirectShowDeviceParser
{
    [GeneratedRegex(
        "^\\s*\\[[^\\]]+\\]\\s*\"(?<name>.+?)\"(?:\\s+\\((?<kind>video|audio)\\))?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DeviceNameLineRegex();

    public static IReadOnlyList<string> ParseVideoDeviceNames(string? ffmpegOutput)
    {
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
        {
            return Array.Empty<string>();
        }

        var devices = new List<string>();
        var inVideoSection = false;

        foreach (var line in ffmpegOutput.Split(Environment.NewLine, StringSplitOptions.None))
        {
            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = true;
                continue;
            }

            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = false;
                continue;
            }

            var match = DeviceNameLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var kind = match.Groups["kind"].Value;
            if (!inVideoSection && !kind.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name)
                && !devices.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                devices.Add(name);
            }
        }

        return devices;
    }
}
