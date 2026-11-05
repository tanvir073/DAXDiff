using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DaxDiff.App.Services;
using DaxDiff.Presentation.Services;

namespace DaxDiff.App;

/// <summary>Shows an enum value in the UI language (see <see cref="EnumText"/>).</summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? EnumText.Of(e) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A CSS colour from a theme token (#rrggbb or rgba()) as a brush, for the theme previews in Settings.</summary>
public sealed class CssBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string css ? ThemeBrushes.Brush(css) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True for a number above zero, for example to show a problem count only when there are problems.</summary>
public sealed class IsPositiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IConvertible n && System.Convert.ToDouble(n, CultureInfo.InvariantCulture) > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Binds a radio button to one value of an enum property (ConverterParameter = the value's name).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null
            ? Enum.Parse(Nullable.GetUnderlyingType(targetType) ?? targetType, parameter.ToString()!)
            : BindingOperations.DoNothing;
}
