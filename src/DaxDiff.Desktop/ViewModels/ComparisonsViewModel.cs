using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>
/// The Comparisons page: create, edit, duplicate and delete comparisons; insert any column (or a whole table's columns)
/// from either model; mark keys; pick aggregations; and assign synonyms that pair the two sides (FR-006, FR-017, FR-018).
/// </summary>
public sealed partial class ComparisonsViewModel : ObservableObject
{
    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private readonly AutoMatcher _matcher;
    private readonly IDialogService _dialogs;
    private readonly Func<ComparisonDefinition, bool, ComparisonEditorViewModel> _createEditor;

    public ComparisonsViewModel(
        SourcePanelViewModel sourceA,
        SourcePanelViewModel sourceB,
        AutoMatcher matcher,
        IDialogService dialogs,
        Func<ComparisonDefinition, bool, ComparisonEditorViewModel> createEditor)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        _matcher = matcher;
        _dialogs = dialogs;
        _createEditor = createEditor;
        _sourceA.MetadataChanged += (_, _) => OnMetadataChanged();
        _sourceB.MetadataChanged += (_, _) => OnMetadataChanged();
        Comparisons.CollectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when a comparison is added, removed or edited.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a comparison is saved or deleted: the project file must be written (FR-021).</summary>
    public event EventHandler? PersistRequested;

    public ObservableCollection<ComparisonEditorViewModel> Comparisons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateComparisonCommand), nameof(DeleteComparisonCommand), nameof(MatchSynonymsCommand), nameof(AddMatchingFieldsCommand))]
    private ComparisonEditorViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private IReadOnlyList<TableNode> _treeA = [];

    [ObservableProperty]
    private IReadOnlyList<TableNode> _treeB = [];

    [ObservableProperty]
    private string _searchA = "";

    [ObservableProperty]
    private string _searchB = "";

    [ObservableProperty]
    private bool _showHidden;

    /// <summary>Selected tree node: a <see cref="TableNode"/> or a <see cref="FieldNode"/>.</summary>
    [ObservableProperty]
    private object? _selectedNodeA;

    [ObservableProperty]
    private object? _selectedNodeB;

    public string HeaderA => _sourceA.Identity;
    public string HeaderB => _sourceB.Identity;

    partial void OnSearchAChanged(string value) => RebuildTrees();

    partial void OnSearchBChanged(string value) => RebuildTrees();

    partial void OnShowHiddenChanged(bool value) => RebuildTrees();

    public void RebuildTrees()
    {
        TreeA = FieldTree.Build(_sourceA.Metadata, SearchA, ShowHidden);
        TreeB = FieldTree.Build(_sourceB.Metadata, SearchB, ShowHidden);
        OnPropertyChanged(nameof(HeaderA));
        OnPropertyChanged(nameof(HeaderB));
    }

    private void OnMetadataChanged()
    {
        RebuildTrees();
        foreach (var comparison in Comparisons)
            comparison.RefreshMetadata();
    }

    // ---- Project load / save --------------------------------------------------------------------

    public void Load(IEnumerable<ComparisonDefinition> definitions)
    {
        Comparisons.Clear();
        foreach (var definition in definitions)
            Add(definition, isSaved: true);
        Selected = Comparisons.FirstOrDefault();
        RebuildTrees();
    }

    /// <summary>The saved version of each comparison, as written to the project file. Never-saved drafts are left out.</summary>
    public IReadOnlyList<ComparisonDefinition> SavedDefinitions() =>
        Comparisons.Where(c => c.SavedDefinition is not null).Select(c => c.SavedDefinition!).ToList();

    public IReadOnlyList<ComparisonEditorViewModel> UnsavedComparisons => Comparisons.Where(c => c.IsModified).ToList();

    /// <summary>Saves every draft (without writing the file; the caller saves the project).</summary>
    public void CommitAll()
    {
        foreach (var comparison in Comparisons.Where(c => c.IsModified))
            comparison.Commit();
    }

    public void SwapSides()
    {
        foreach (var comparison in Comparisons)
            comparison.SwapSides();
    }

    private ComparisonEditorViewModel Add(ComparisonDefinition definition, bool isSaved)
    {
        var editor = _createEditor(definition, isSaved);
        editor.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        editor.SaveRequested += (_, _) => PersistRequested?.Invoke(this, EventArgs.Empty);
        Comparisons.Add(editor);
        return editor;
    }

    // ---- Comparison list ------------------------------------------------------------------------

    [RelayCommand]
    private void NewComparison()
    {
        var number = Comparisons.Count + 1;
        while (Exists($"Comparison {number}"))
            number++;
        Selected = Add(new ComparisonDefinition { Name = $"Comparison {number}" }, isSaved: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateComparison()
    {
        if (Selected is null)
            return;
        var copy = Selected.ToDefinition() with { Id = Guid.NewGuid(), Name = UniqueName(Selected.Name + " copy") };
        Selected = Add(copy, isSaved: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteComparison()
    {
        if (Selected is null || !_dialogs.Confirm($"Delete the comparison '{Selected.Name}'?"))
            return;
        var index = Comparisons.IndexOf(Selected);
        var wasSaved = Selected.IsSaved;
        Comparisons.Remove(Selected);
        Selected = Comparisons.Count == 0 ? null : Comparisons[Math.Min(index, Comparisons.Count - 1)];
        if (wasSaved)
            PersistRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool Exists(string name) => Comparisons.Any(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

    private string UniqueName(string candidate)
    {
        var name = candidate;
        for (var i = 2; Exists(name); i++)
            name = $"{candidate} {i}";
        return name;
    }

    // ---- Inserting fields -----------------------------------------------------------------------

    [RelayCommand]
    private void InsertA() => Insert(Side.A, SelectedNodeA);

    [RelayCommand]
    private void InsertB() => Insert(Side.B, SelectedNodeB);

    /// <summary>Inserts the selected field, or every column of the selected table, into one side of the current comparison.</summary>
    public void Insert(Side side, object? node)
    {
        if (node is null)
        {
            _dialogs.ShowInfo($"Select a table or a field in the model {side} tree first.");
            return;
        }

        if (Selected is null)
            NewComparison();
        var target = Selected!.FieldsOf(side);

        var candidates = node switch
        {
            TableNode table => table.Fields.Where(f => !f.IsMeasure).ToList(),
            FieldNode field => [field],
            _ => [],
        };

        var skipped = 0;
        foreach (var field in candidates)
        {
            if (target.Any(f => f.Field == field.Ref))
            {
                skipped++;
                continue;
            }

            target.Add(ComparisonFieldViewModel.Insert(field.Ref, field.DataKind));
        }

        if (skipped > 0 && skipped == candidates.Count)
            _dialogs.ShowInfo(candidates.Count == 1 ? "That field is already in the comparison." : "All those columns are already in the comparison.");
    }

    // ---- Editing inserted fields ----------------------------------------------------------------

    /// <summary>Removes the selected fields (several can be selected in a grid).</summary>
    [RelayCommand]
    private void RemoveFields(IList? fields)
    {
        if (Selected is null || fields is null)
            return;
        foreach (var field in fields.OfType<ComparisonFieldViewModel>().ToList())
        {
            if (!Selected.FieldsA.Remove(field))
                Selected.FieldsB.Remove(field);
        }
    }

    [RelayCommand]
    private void CopyToB(IList? fields) => CopySameFields(fields, Side.B);

    [RelayCommand]
    private void CopyToA(IList? fields) => CopySameFields(fields, Side.A);

    /// <summary>
    /// Adds the same fields to the other model: same table and column name (measures by name), keeping key, aggregation
    /// and synonym so each pairs with its original. Fields the other model does not have are listed, not guessed.
    /// </summary>
    private void CopySameFields(IList? fields, Side target)
    {
        if (Selected is null)
            return;
        var source = target == Side.B ? Side.A : Side.B;
        var chosen = fields?.OfType<ComparisonFieldViewModel>().ToList() ?? [];
        if (chosen.Count == 0)
        {
            _dialogs.ShowInfo($"Select one or more fields in the model {source} grid first (Ctrl+click or Shift+click to select several).");
            return;
        }

        if (Selected.MetadataOf(target) is not { } meta)
        {
            _dialogs.ShowInfo($"Connect model {target} first (Sources page) so its tables and columns are known.");
            return;
        }

        var destination = Selected.FieldsOf(target);
        var added = 0;
        var already = 0;
        var missing = new List<string>();
        foreach (var field in chosen)
        {
            FieldRef? match = field.IsMeasure
                ? meta.FindMeasure(field.Field.Name)?.Ref
                : meta.FindColumn(field.Field.Table, field.Field.Name)?.Ref;
            if (match is null)
            {
                missing.Add(field.FieldText);
                continue;
            }

            if (destination.Any(f => f.Field == match))
            {
                already++;
                continue;
            }

            var kind = meta.KindOf(match);
            var aggregation = AggregationRules.IsAllowed(field.Aggregation, match.Kind, kind, field.IsKey)
                ? field.Aggregation
                : AggregationRules.Default(match.Kind, kind, field.IsKey);
            destination.Add(new ComparisonFieldViewModel(match, kind, field.IsKey, aggregation, field.Synonym));
            added++;
        }

        if (missing.Count > 0 || added == 0)
        {
            var message = $"{added} field(s) added to model {target}." + (already > 0 ? $" {already} were already there." : "");
            if (missing.Count > 0)
                message += $"{Environment.NewLine}{Environment.NewLine}Not found in model {target} (no table and column with the same name):{Environment.NewLine}  " +
                           string.Join(Environment.NewLine + "  ", missing);
            _dialogs.ShowInfo(message, "Add same columns");
        }
    }

    [RelayCommand]
    private void MoveFieldUp(ComparisonFieldViewModel? field) => Move(field, -1);

    [RelayCommand]
    private void MoveFieldDown(ComparisonFieldViewModel? field) => Move(field, 1);

    private void Move(ComparisonFieldViewModel? field, int delta)
    {
        if (field is null || Selected is null)
            return;
        var list = Selected.FieldsA.Contains(field) ? Selected.FieldsA : Selected.FieldsB;
        var index = list.IndexOf(field);
        if (index >= 0 && index + delta >= 0 && index + delta < list.Count)
            list.Move(index, index + delta);
    }

    /// <summary>Gives unpaired model B fields the synonym of their one equivalent model A field (FR-007, FR-018).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MatchSynonyms()
    {
        if (Selected is null)
            return;

        var result = _matcher.SuggestSynonyms(
            Selected.FieldsA.Select(f => f.ToModel()).ToList(),
            Selected.FieldsB.Select(f => f.ToModel()).ToList());
        foreach (var match in result.Matches)
            Selected.FieldsB[match.IndexB].Synonym = match.Synonym;

        var message = result.Matches.Count == 0
            ? "No new matches found. Synonyms are matched by equivalent names and the synonym dictionary."
            : $"{result.Matches.Count} model B synonym(s) set:{Environment.NewLine}" +
              string.Join(Environment.NewLine, result.Matches.Select(m => $"  {m.FieldB} → '{m.Synonym}' (pairs with {m.FieldA})"));
        if (result.Ambiguous.Count > 0)
            message += $"{Environment.NewLine}{Environment.NewLine}Ambiguous, set these by hand:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", result.Ambiguous);
        _dialogs.ShowInfo(message, "Match synonyms");
    }

    /// <summary>
    /// For each model A field without a counterpart, finds the equivalent model B field in B's metadata and inserts it
    /// with the same synonym, key flag and (where B's type allows) aggregation. Ambiguous candidates are listed, not guessed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddMatchingFields()
    {
        if (Selected is null)
            return;
        if (_sourceA.Metadata is not { } a || _sourceB.Metadata is not { } b)
        {
            _dialogs.ShowInfo("Connect both models first (Sources page).");
            return;
        }

        var pairedB = Selected.FieldsB.Select(f => ComparisonCompiler.SynonymKey(f.Synonym)).ToHashSet();
        var added = new List<string>();
        var unresolved = new List<string>();
        foreach (var field in Selected.FieldsA.Where(f => !pairedB.Contains(ComparisonCompiler.SynonymKey(f.Synonym))).ToList())
        {
            var suggestion = field.IsMeasure ? _matcher.SuggestMeasure(field.Field, b) : _matcher.SuggestColumn(field.Field, a, b);
            if (suggestion.Right is not { } right || Selected.FieldsB.Any(f => f.Field == right))
            {
                unresolved.Add(suggestion.IsAmbiguous
                    ? $"{field.Field}: {string.Join(", ", suggestion.Candidates)}"
                    : $"{field.Field}: no equivalent found");
                continue;
            }

            var kind = b.KindOf(right);
            var aggregation = AggregationRules.IsAllowed(field.Aggregation, right.Kind, kind, field.IsKey)
                ? field.Aggregation
                : AggregationRules.Default(right.Kind, kind, field.IsKey);
            Selected.FieldsB.Add(new ComparisonFieldViewModel(right, kind, field.IsKey, aggregation, field.Synonym));
            added.Add($"{right} as '{field.Synonym}'");
        }

        var message = added.Count == 0 ? "No model B fields were added." : $"Added {added.Count} model B field(s):{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", added);
        if (unresolved.Count > 0)
            message += $"{Environment.NewLine}{Environment.NewLine}Not matched, insert these by hand:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", unresolved);
        _dialogs.ShowInfo(message, "Add matching model B fields");
    }
}
