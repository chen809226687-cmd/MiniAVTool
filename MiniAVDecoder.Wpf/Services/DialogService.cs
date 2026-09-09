using System.Diagnostics;
using System.IO;
using Forms = System.Windows.Forms;

namespace MiniAVDecoder.Wpf.Services;

public sealed class DialogService : IDialogService
{
    public string? PickVideoFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "媒体文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m4v|所有文件|*.*",
            Title = "选择视频文件"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialPath) ? initialPath : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }
}
