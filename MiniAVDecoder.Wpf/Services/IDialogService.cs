using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MiniAVDecoder.Wpf.Services;

public interface IDialogService
{
    string? PickVideoFile();
    string? PickFolder(string? initialPath);
    void OpenFolder(string path);
}
