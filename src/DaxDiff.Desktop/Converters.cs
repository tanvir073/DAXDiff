using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.Desktop;

/// <summary>
/// Shows an enum value in the UI language: the Strings resource "Enum_&lt;Type&gt;_&lt;Value&gt;", else the value's name.
/// </summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is Enum e ? Text(e) : value;

    public static string Text(Enum value) =>
        Resources.Strings.ResourceManager.GetString($"Enum_{value.GetType().Name}_{value}") ?? value.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Corner radius of half the element's height, for pill shapes. WPF scales oversized radii per axis,
/// which turns a large fixed radius into an ellipse.
/// </summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double height && height > 0 ? height / 2 : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Binds a radio button to one value of an enum property.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
