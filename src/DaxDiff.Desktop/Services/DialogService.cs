using System.Windows;
using DaxDiff.Domain;
using Microsoft.Win32;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.Services;

public interface IDialogService
{
    void ShowError(Exception exception, string? context = null);
    void ShowInfo(string message, string title = "DAXDiff");
    bool Confirm(string message, string title = "DAXDiff");

    /// <summary>Yes = true, No = false, Cancel = null.</summary>
    bool? AskYesNoCancel(string message, string title = "DAXDiff");
    string? OpenFile(string filter, string? title = null);
    string? SaveFile(string filter, string defaultFileName, string? title = null);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => System.Windows.Application.Current?.MainWindow;

    public void ShowError(Exception exception, string? context = null)
    {
        var text = exception is DaxDiffException known
            ? known.ToUserText()
            : exception.Message + Environment.NewLine + Localized.Format(Strings.Dialog_DiagnosticCode, "ERR-UNEXPECTED");
        if (context is not null)
            text = context + Environment.NewLine + Environment.NewLine + text;
        Show(text, Strings.Dialog_ProblemTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowInfo(string message, string title = "DAXDiff") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message, string title = "DAXDiff") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? AskYesNoCancel(string message, string title = "DAXDiff") =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public string? OpenFile(string filter, string? title = null)
    {
        var dialog = new OpenFileDialog { Filter = filter, Title = title ?? Strings.Dialog_Open };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string filter, string defaultFileName, string? title = null)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName, Title = title ?? Strings.Dialog_Save, OverwritePrompt = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    private static MessageBoxResult Show(string text, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, text, title, buttons, image)
            : MessageBox.Show(text, title, buttons, image);
}
