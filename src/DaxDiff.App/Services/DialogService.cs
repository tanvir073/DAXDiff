using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DaxDiff.App.Views;
using DaxDiff.Desktop.Resources;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;

namespace DaxDiff.App.Services;

/// <summary>
/// Message boxes and file pickers. Avalonia's dialogs are asynchronous; the shared view models expect modal calls,
/// so each one runs a nested dispatcher frame until the dialog closes (<see cref="Modal.Run{T}"/>).
/// </summary>
public sealed class DialogService : IDialogService
{
    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void ShowError(Exception exception, string? context = null)
    {
        var text = exception is DaxDiffException known
            ? known.ToUserText()
            : exception.Message + Environment.NewLine + Localized.Format(Strings.Dialog_DiagnosticCode, "ERR-UNEXPECTED");
        if (context is not null)
            text = context + Environment.NewLine + Environment.NewLine + text;
        Show(text, Strings.Dialog_ProblemTitle, MessageButtons.Ok, MessageTone.Warning);
    }

    public void ShowInfo(string message, string title = "DAXDiff") => Show(message, title, MessageButtons.Ok, MessageTone.Info);

    public bool Confirm(string message, string title = "DAXDiff") => Show(message, title, MessageButtons.YesNo, MessageTone.Question) == true;

    public bool? AskYesNoCancel(string message, string title = "DAXDiff") => Show(message, title, MessageButtons.YesNoCancel, MessageTone.Question);

    public string? OpenFile(string filter, string? title = null)
    {
        if (Owner is not { } owner)
            return null;
        var files = Modal.Run(() => owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title ?? Strings.Dialog_Open,
            AllowMultiple = false,
            FileTypeFilter = FileTypes(filter),
        }));
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public string? SaveFile(string filter, string defaultFileName, string? title = null)
    {
        if (Owner is not { } owner)
            return null;
        var types = FileTypes(filter);
        var file = Modal.Run(() => owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title ?? Strings.Dialog_Save,
            SuggestedFileName = defaultFileName,
            DefaultExtension = Path.GetExtension(defaultFileName).TrimStart('.'),
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
        }));
        return file?.TryGetLocalPath();
    }

    private static bool? Show(string text, string title, MessageButtons buttons, MessageTone tone)
    {
        var dialog = new MessageWindow(text, title, buttons, tone);
        return Owner is { IsVisible: true } owner
            ? Modal.Run(() => dialog.ShowDialog<bool?>(owner))
            : Modal.Run(() => dialog.ShowStandaloneAsync());
    }

    /// <summary>Turns a WPF-style filter ("Projects (*.pbicomp)|*.pbicomp|All files (*.*)|*.*") into file types.</summary>
    internal static List<FilePickerFileType> FileTypes(string filter)
    {
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
            types.Add(new FilePickerFileType(parts[i]) { Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) });
        return types;
    }
}

/// <summary>Runs an asynchronous dialog to completion on the UI thread, keeping the UI responsive meanwhile.</summary>
internal static class Modal
{
    public static T Run<T>(Func<Task<T>> show)
    {
        var task = show();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }
}
