using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.Desktop.Services;

public interface IFilterEditorService
{
    /// <summary>Opens the filter pop-up for a comparison; true when the user applied changes.</summary>
    bool Edit(ComparisonEditorViewModel comparison);
}
