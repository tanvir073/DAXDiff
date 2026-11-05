using Avalonia.Controls;
using Avalonia.Input;
using DaxDiff.Presentation.Resources;

namespace DaxDiff.App.Views;

public enum MessageButtons { Ok, YesNo, YesNoCancel }

public enum MessageTone { Info, Question, Warning }

/// <summary>A themed message box. Closes with true (OK/Yes), false (No) or null (Cancel, Esc, or the close button).</summary>
public partial class MessageWindow : Window
{
    private readonly TaskCompletionSource<bool?> _standalone = new();

    public MessageWindow() => InitializeComponent();

    public MessageWindow(string text, string title, MessageButtons buttons, MessageTone tone) : this()
    {
        Title = title;
        Text.Text = text;
        Glyph.Text = tone switch { MessageTone.Warning => "!", MessageTone.Question => "?", _ => "i" };
        if (tone == MessageTone.Warning)
        {
            Tone.Bind(Border.BackgroundProperty, Tone.GetResourceObservable("WarningSoftBrush"));
            Glyph.Bind(TextBlock.ForegroundProperty, Glyph.GetResourceObservable("WarningBrush"));
        }

        switch (buttons)
        {
            case MessageButtons.Ok:
                Add(Strings.Dialog_Ok, true, primary: true, isDefault: true);
                break;
            case MessageButtons.YesNo:
                Add(Strings.Dialog_No, false);
                Add(Strings.Dialog_Yes, true, primary: true, isDefault: true);
                break;
            case MessageButtons.YesNoCancel:
                Add(Strings.Dialog_Cancel, null);
                Add(Strings.Dialog_No, false);
                Add(Strings.Dialog_Yes, true, primary: true, isDefault: true);
                break;
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Finish(buttons == MessageButtons.YesNo ? false : buttons == MessageButtons.Ok ? true : null);
        };
        Closed += (_, _) => _standalone.TrySetResult(null);
    }

    /// <summary>Shows the box without an owner (before the main window is visible).</summary>
    public Task<bool?> ShowStandaloneAsync()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Show();
        return _standalone.Task;
    }

    private void Add(string text, bool? result, bool primary = false, bool isDefault = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, MinWidth = 88 };
        if (primary)
            button.Classes.Add("primary");
        button.Click += (_, _) => Finish(result);
        Buttons.Children.Add(button);
    }

    private void Finish(bool? result)
    {
        _standalone.TrySetResult(result);
        Close(result);
    }
}
