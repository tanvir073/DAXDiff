using System.ComponentModel;
using System.Windows;
using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Offers to save unsaved project changes; Cancel keeps the window open.
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel main && !main.ConfirmClose())
            e.Cancel = true;
    }
}
