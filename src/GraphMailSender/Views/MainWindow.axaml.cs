using Avalonia.Controls;
using GraphMailSender.ViewModels;

namespace GraphMailSender.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
