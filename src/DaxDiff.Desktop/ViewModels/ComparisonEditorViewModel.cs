using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DaxDiff.Comparison;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>A pair as it will run: what is compared with what, under which synonym.</summary>
public sealed record PairPreview(string Synonym, string Role, string A, string B);

/// <summary>
/// One comparison being edited: the fields inserted on each side, their keys, aggregations and synonyms,
/// and its own options. Recompiles on every change so pairing and validation stay current.
/// </summary>
public sealed partial class ComparisonEditorViewModel : ObservableObject
{
    private static readonly string[] OptionsOutputs =
        [nameof(OptionsViewModel.QueryPreviewA), nameof(OptionsViewModel.QueryPreviewB), nameof(OptionsViewModel.EstimateText)];

    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private bool _suspended;

    public ComparisonEditorViewModel(ComparisonDefinition definition, SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, OptionsViewModel options)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        Id = definition.Id;
        _name = definition.Name;
        Options = options;
        Options.CompiledProvider = () => Compiled;

        FieldsA.CollectionChanged += OnFieldsChanged;
        FieldsB.CollectionChanged += OnFieldsChanged;
        Options.PropertyChanged += (_, e) =>
        {
            if (!OptionsOutputs.Contains(e.PropertyName))
                Recompile();
        };
        Options.Filters.CollectionChanged += (_, e) =>
        {
            foreach (var row in (e.NewItems ?? Array.Empty<FilterRowViewModel>()).OfType<FilterRowViewModel>())
                row.PropertyChanged += (_, _) => Recompile();
            Recompile();
        };

        Load(definition);
    }

    /// <summary>Raised after any edit, once the comparison has been recompiled.</summary>
    public event EventHandler? Changed;

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
        ? $"{ErrorCount} error(s) to fix"
        : $"{Compiled.Config.Keys.Count} key(s), {Compiled.Config.Values.Count} value(s) compared";

    partial void OnNameChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<ComparisonFieldViewModel> FieldsOf(Side side) => side == Side.A ? FieldsA : FieldsB;

    public ModelMetadata? MetadataOf(Side side) => side == Side.A ? _sourceA.Metadata : _sourceB.Metadata;

    public ComparisonDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name.Trim().Length == 0 ? "Unnamed comparison" : Name.Trim(),
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
            .Select(p => new PairPreview(p.Alias, p.Role == FieldRole.Key ? "Key" : "Value", Describe(p.Left, p.LeftAggregation), Describe(p.Right, p.RightAggregation)))
            .ToList();
        Annotate(FieldsA, FieldsB, Side.A);
        Annotate(FieldsB, FieldsA, Side.B);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(FieldRef field, Aggregation aggregation) =>
        aggregation == Aggregation.None ? field.ToString() : $"{aggregation}({field})";

    private void Annotate(IEnumerable<ComparisonFieldViewModel> fields, IReadOnlyList<ComparisonFieldViewModel> others, Side side)
    {
        var other = side == Side.A ? "B" : "A";
        foreach (var field in fields)
        {
            var key = ComparisonCompiler.SynonymKey(field.Synonym);
            var match = key.Length == 0 ? null : others.FirstOrDefault(o => ComparisonCompiler.SynonymKey(o.Synonym) == key);
            field.PairText = match is null
                ? $"No model {other} field named '{field.Synonym.Trim()}': not compared"
                : $"↔ {match.FieldText}{(match.Aggregation == Aggregation.None ? "" : $" ({match.Aggregation})")}";

            var mine = Compiled.Issues
                .Where(i => i.Alias is not null && ComparisonCompiler.SynonymKey(i.Alias) == key)
                .Where(i => !i.Message.StartsWith("Model ", StringComparison.Ordinal) || i.Message.StartsWith($"Model {side}", StringComparison.Ordinal))
                .DistinctBy(i => i.Message)
                .ToList();
            field.Issue = mine.Count == 0 ? null : string.Join(" ", mine.Select(i => i.Message));
            field.HasError = mine.Any(i => i.Severity == IssueSeverity.Error);
        }
    }
}
