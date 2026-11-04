using Avalonia.Controls;
using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Offers to save unsaved project changes; Cancel keeps the window open.
        Closing += (_, e) =>
        {
            if (DataContext is MainViewModel main && !main.ConfirmClose())
                e.Cancel = true;
        };
    }
}
