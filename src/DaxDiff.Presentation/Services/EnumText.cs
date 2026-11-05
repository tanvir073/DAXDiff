namespace DaxDiff.Presentation.Services;

/// <summary>
/// An enum value in the UI language: the Strings resource "Enum_&lt;Type&gt;_&lt;Value&gt;", else the value's name.
/// </summary>
public static class EnumText
{
    public static string Of(Enum value) =>
        Resources.Strings.ResourceManager.GetString($"Enum_{value.GetType().Name}_{value}") ?? value.ToString();
}
