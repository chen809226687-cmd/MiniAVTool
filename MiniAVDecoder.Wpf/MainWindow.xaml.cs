using System.Windows;
using MiniAVDecoder.Wpf.ViewModels;

namespace MiniAVDecoder.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
