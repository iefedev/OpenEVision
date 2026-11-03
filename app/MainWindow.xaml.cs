using OpenEVision.Pages;
using Wpf.Ui.Controls;

namespace OpenEVision;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Nav.Navigate(typeof(DevicePage));
    }

    public void NavigateTo(Type page) => Nav.Navigate(page);
}
