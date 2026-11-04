namespace DaxDiff.Desktop.Services;

/// <summary>Message boxes and file pickers. Modal: each call returns once the user has answered.</summary>
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
