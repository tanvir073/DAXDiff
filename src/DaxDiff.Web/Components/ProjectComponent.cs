using DaxDiff.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DaxDiff.Web.Components;

/// <summary>Base for components that show project state: re-renders whenever the project, a source or a run changes.</summary>
public abstract class ProjectComponent : ComponentBase, IDisposable
{
    [Inject]
    protected ProjectState State { get; set; } = default!;

    protected override void OnInitialized() => State.Changed += OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose() => State.Changed -= OnChanged;
}
