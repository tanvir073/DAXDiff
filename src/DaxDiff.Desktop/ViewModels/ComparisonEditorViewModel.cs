using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Domain;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>A pair as it will run: what is compared with what, under which synonym.</summary>
public sealed record PairPreview(string Synonym, string Role, string A, string B);

/// <summary>
/// One comparison being edited: the fields inserted on each side, their keys, aggregations and synonyms,
/// and its own options. Recompiles on every change so pairing and validation stay current.
/// Edits are a draft until saved: runs and the project file only ever use the last saved version (FR-021).
/// </summary>
public sealed partial class ComparisonEditorViewModel : ObservableObject
{
    private static readonly string[] OptionsOutputs =
        [nameof(OptionsViewModel.QueryPreviewA), nameof(OptionsViewModel.QueryPreviewB), nameof(OptionsViewModel.EstimateText)];

    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private bool _suspended;

    /// <param name="isSaved">True when <paramref name="definition"/> is already saved (loaded from the project); false for a new draft.</param>
    public ComparisonEditorViewModel(ComparisonDefinition definition, bool isSaved, SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, OptionsViewModel options)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        Id = definition.Id;
        _name = definition.Name;
        Options = options;
        Options.CompiledProvider = () => Compiled;
        _savedDefinition = isSaved ? definition : null;

        FieldsA.CollectionChanged += OnFieldsChanged;
        FieldsB.CollectionChanged += OnFieldsChanged;
        Options.PropertyChanged += (_, e) =>
        {
            if (!OptionsOutputs.Contains(e.PropertyName))
                Recompile();
        };
        Options.Filters.CollectionChanged += (_, _) => Recompile();

        Load(definition);
    }

    /// <summary>Raised after any edit, once the comparison has been recompiled.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the user saves this comparison; the shell then writes the project file.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>The version that runs and is written to the project file; null until first saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(RunStatusText), nameof(DisplayName))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    private ComparisonDefinition? _savedDefinition;

    /// <summary>The draft differs from the saved version (or was never saved).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunStatusText), nameof(DisplayName), nameof(SaveStateText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardChangesCommand))]
    private bool _isModified;

    public bool IsSaved => SavedDefinition is not null;

    /// <summary>Name for lists, with a marker while there are unsaved changes.</summary>
    public string DisplayName => IsModified ? $"{Name} *" : Name;

    public string SaveStateText => IsModified
        ? IsSaved ? Strings.Editor_UnsavedChanges : Strings.Editor_NotSavedYet
        : Strings.Editor_Saved;

    /// <summary>Status on the Run page, describing the saved version that will run.</summary>
    public string RunStatusText
    {
        get
        {
            if (SavedDefinition is null)
                return Strings.Editor_NotSavedRun;
            var saved = ComparisonCompiler.CompileAndValidate(SavedDefinition, _sourceA.Metadata, _sourceB.Metadata);
            var errors = saved.Issues.Count(i => i.Severity == IssueSeverity.Error);
            var text = errors > 0
                ? Localized.Format(Strings.Editor_SavedVersionErrors, errors)
                : Localized.Format(Strings.Editor_KeysValues, saved.Config.Keys.Count, saved.Config.Values.Count);
            return IsModified ? text + Strings.Editor_NewerChangesSuffix : text;
        }
    }

    private bool CanSave() => IsModified;

    /// <summary>Makes the draft the saved version and asks the shell to write the project file.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Commit();
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes the draft the saved version without writing the file (the caller saves the project).</summary>
    public void Commit()
    {
        SavedDefinition = ToDefinition();
        UpdateModified();
    }

    private bool CanDiscard() => IsModified && IsSaved;

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void DiscardChanges()
    {
        if (SavedDefinition is not null)
            Load(SavedDefinition);
    }

    private static string Fingerprint(ComparisonDefinition definition) => JsonSerializer.Serialize(definition, ProjectStore.JsonOptions);

    private void UpdateModified() =>
        IsModified = SavedDefinition is null || Fingerprint(ToDefinition()) != Fingerprint(SavedDefinition);

    /// <summary>What the filter pop-up needs: both models and this comparison's synonym pairs.</summary>
    public FilterEditorContext FilterContext() =>
        new(_sourceA.Metadata, _sourceB.Metadata, _sourceA.Connector, _sourceB.Connector, Compiled.Config.FieldPairs);

    public Guid Id { get; }

    [ObservableProperty]
    private string _name;

    /// <summary>Whether the Run page includes this comparison.</summary>
    [ObservableProperty]
    private bool _includeInRun = true;

    public ObservableCollection<ComparisonFieldViewModel> FieldsA { get; } = [];
    public ObservableCollection<ComparisonFieldViewModel> FieldsB { get; } = [];
    public OptionsViewModel Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorCount), nameof(StatusText))]
    private CompiledComparison _compiled = new(new ComparisonConfig(), []);

    [ObservableProperty]
    private IReadOnlyList<PairPreview> _pairs = [];

    public int ErrorCount => Compiled.Issues.Count(i => i.Severity == IssueSeverity.Error);

    public string StatusText => ErrorCount > 0
        ? Localized.Format(Strings.Editor_ErrorsToFix, ErrorCount)
        : Localized.Format(Strings.Editor_KeysValuesCompared, Compiled.Config.Keys.Count, Compiled.Config.Values.Count);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        Recompile();
    }

    public ObservableCollection<ComparisonFieldViewModel> FieldsOf(Side side) => side == Side.A ? FieldsA : FieldsB;

    public ModelMetadata? MetadataOf(Side side) => side == Side.A ? _sourceA.Metadata : _sourceB.Metadata;

    public ComparisonDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name.Trim().Length == 0 ? Strings.Editor_UnnamedComparison : Name.Trim(),
        FieldsA = FieldsA.Select(f => f.ToModel()).ToList(),
        FieldsB = FieldsB.Select(f => f.ToModel()).ToList(),
        Options = Options.ToOptions(),
    };

    /// <summary>Re-reads data types after a model was (re)connected, keeping every key, aggregation and synonym.</summary>
    public void RefreshMetadata() => Load(ToDefinition());

    /// <summary>Exchanges the model A and model B fields and filter columns, for when the sources are swapped.</summary>
    public void SwapSides()
    {
        var definition = ToDefinition();
        Load(definition with
        {
            FieldsA = definition.FieldsB,
            FieldsB = definition.FieldsA,
            Options = definition.Options with
            {
                Filters = definition.Options.Filters.Select(f => f with { LeftColumn = f.RightColumn, RightColumn = f.LeftColumn }).ToList(),
            },
        });
    }

    private void Load(ComparisonDefinition definition)
    {
        _suspended = true;
        try
        {
            Name = definition.Name;
            FieldsA.Clear();
            FieldsB.Clear();
            foreach (var field in definition.FieldsA)
                FieldsA.Add(ComparisonFieldViewModel.From(field, _sourceA.Metadata));
            foreach (var field in definition.FieldsB)
                FieldsB.Add(ComparisonFieldViewModel.From(field, _sourceB.Metadata));
            Options.Load(definition.Options);
        }
        finally
        {
            _suspended = false;
        }

        Recompile();
    }

    private void OnFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
        {
            foreach (ComparisonFieldViewModel field in e.NewItems!)
                field.PropertyChanged += OnFieldPropertyChanged;
        }

        Recompile();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Pair text and issues are written by Recompile itself.
        if (e.PropertyName is not (nameof(ComparisonFieldViewModel.PairText) or nameof(ComparisonFieldViewModel.Issue)
            or nameof(ComparisonFieldViewModel.HasError) or nameof(ComparisonFieldViewModel.AllowedAggregations) or nameof(ComparisonFieldViewModel.CanAggregate)))
            Recompile();
    }

    public void Recompile()
    {
        if (_suspended)
            return;

        Compiled = ComparisonCompiler.CompileAndValidate(ToDefinition(), _sourceA.Metadata, _sourceB.Metadata);
        Pairs = Compiled.Config.FieldPairs
            .Select(p => new PairPreview(p.Alias, EnumTextConverter.Text(p.Role), Describe(p.Left, p.LeftAggregation), Describe(p.Right, p.RightAggregation)))
            .ToList();
        Annotate(FieldsA, FieldsB, Side.A);
        Annotate(FieldsB, FieldsA, Side.B);
        UpdateModified();
        OnPropertyChanged(nameof(RunStatusText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(FieldRef field, Aggregation aggregation) =>
        aggregation == Aggregation.None ? field.ToString() : $"{EnumTextConverter.Text(aggregation)}({field})";

    private void Annotate(IEnumerable<ComparisonFieldViewModel> fields, IReadOnlyList<ComparisonFieldViewModel> others, Side side)
    {
        var other = side == Side.A ? "B" : "A";
        foreach (var field in fields)
        {
            var key = ComparisonCompiler.SynonymKey(field.Synonym);
            var match = key.Length == 0 ? null : others.FirstOrDefault(o => ComparisonCompiler.SynonymKey(o.Synonym) == key);
            field.PairText = match is null
                ? Localized.Format(Strings.Editor_NoCounterpart, other, field.Synonym.Trim())
                : $"↔ {match.FieldText}{(match.Aggregation == Aggregation.None ? "" : $" ({EnumTextConverter.Text(match.Aggregation)})")}";

            var mine = Compiled.Issues
                .Where(i => i.Alias is not null && ComparisonCompiler.SynonymKey(i.Alias) == key)
                .Where(i => i.Side is null || i.Side == side)
                .DistinctBy(i => i.Message)
                .ToList();
            field.Issue = mine.Count == 0 ? null : string.Join(" ", mine.Select(i => i.Message));
            field.HasError = mine.Any(i => i.Severity == IssueSeverity.Error);
        }
    }
}
