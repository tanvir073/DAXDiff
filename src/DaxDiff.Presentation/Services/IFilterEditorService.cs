using DaxDiff.Presentation.ViewModels;

namespace DaxDiff.Presentation.Services;

public interface IFilterEditorService
{
    /// <summary>Opens the filter pop-up for a comparison; true when the user applied changes.</summary>
    bool Edit(ComparisonEditorViewModel comparison);
}
