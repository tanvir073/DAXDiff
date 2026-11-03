using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using DaxDiff.Desktop.Resources;

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

    /// <summary>All selected tree nodes (Ctrl+click and Shift+click select several), in tree order.</summary>
    public IReadOnlyList<object> SelectedNodesA { get; set; } = [];

    public IReadOnlyList<object> SelectedNodesB { get; set; } = [];

    public string HeaderA => _sourceA.Identity;
    public string HeaderB => _sourceB.Identity;

    // Search in each field grid's header: matches the field name or its type. It only hides rows; the comparison
    // keeps every field.
    [ObservableProperty]
    private string _fieldSearchA = "";

    [ObservableProperty]
    private string _fieldSearchB = "";

    partial void OnFieldSearchAChanged(string value) => ApplyFieldSearch();

    partial void OnFieldSearchBChanged(string value) => ApplyFieldSearch();

    partial void OnSelectedChanged(ComparisonEditorViewModel? oldValue, ComparisonEditorViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.FieldsA.CollectionChanged -= OnSelectedFieldsChanged;
            oldValue.FieldsB.CollectionChanged -= OnSelectedFieldsChanged;
        }

        if (newValue is not null)
        {
            newValue.FieldsA.CollectionChanged += OnSelectedFieldsChanged;
            newValue.FieldsB.CollectionChanged += OnSelectedFieldsChanged;
        }

        ApplyFieldSearch();
        MarkInComparison();
    }

    private void OnSelectedFieldsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => MarkInComparison();

    // Fields already in the selected comparison are left out of the trees, so only new ones are offered.
    private void MarkInComparison()
    {
        FieldTree.MarkInComparison(TreeA, (Selected?.FieldsA.Select(f => f.Field) ?? []).ToHashSet());
        FieldTree.MarkInComparison(TreeB, (Selected?.FieldsB.Select(f => f.Field) ?? []).ToHashSet());

        // A node that was just inserted is no longer shown, so it is no longer selected either.
        SelectedNodesA = SelectedNodesA.Where(FieldTree.IsOffered).ToList();
        SelectedNodesB = SelectedNodesB.Where(FieldTree.IsOffered).ToList();
        if (SelectedNodeA is { } a && !FieldTree.IsOffered(a))
            SelectedNodeA = null;
        if (SelectedNodeB is { } b && !FieldTree.IsOffered(b))
            SelectedNodeB = null;
    }

    private void ApplyFieldSearch()
    {
        if (Selected is null)
            return;
        SetSearch(Selected.FieldsA, FieldSearchA);
        SetSearch(Selected.FieldsB, FieldSearchB);
    }

    private static void SetSearch(IEnumerable fields, string text)
    {
        var view = CollectionViewSource.GetDefaultView(fields);
        text = text.Trim();
        view.Filter = text.Length == 0
            ? null
            : item => item is ComparisonFieldViewModel f &&
                      (f.FieldText.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
                       f.TypeText.Contains(text, StringComparison.CurrentCultureIgnoreCase));
    }

    partial void OnSearchAChanged(string value) => RebuildTrees();

    partial void OnSearchBChanged(string value) => RebuildTrees();

    partial void OnShowHiddenChanged(bool value) => RebuildTrees();

    public void RebuildTrees()
    {
        TreeA = FieldTree.Build(_sourceA.Metadata, SearchA, ShowHidden);
        TreeB = FieldTree.Build(_sourceB.Metadata, SearchB, ShowHidden);
        MarkInComparison();
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
        while (Exists(Localized.Format(Strings.Comparisons_DefaultName, number)))
            number++;
        Selected = Add(new ComparisonDefinition { Name = Localized.Format(Strings.Comparisons_DefaultName, number) }, isSaved: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateComparison()
    {
        if (Selected is null)
            return;
        var copy = Selected.ToDefinition() with { Id = Guid.NewGuid(), Name = UniqueName(Localized.Format(Strings.Comparisons_CopyName, Selected.Name)) };
        Selected = Add(copy, isSaved: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteComparison()
    {
        if (Selected is null || !_dialogs.Confirm(Localized.Format(Strings.Comparisons_DeleteQuestion, Selected.Name)))
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
    private void InsertA() => Insert(Side.A, SelectedNodesA.Count > 0 ? SelectedNodesA : [.. Single(SelectedNodeA)]);

    [RelayCommand]
    private void InsertB() => Insert(Side.B, SelectedNodesB.Count > 0 ? SelectedNodesB : [.. Single(SelectedNodeB)]);

    private static IEnumerable<object> Single(object? node) => node is null ? [] : [node];

    /// <summary>Inserts the selected field, or every column of the selected table, into one side of the current comparison.</summary>
    public void Insert(Side side, object? node) => Insert(side, [.. Single(node)]);

    /// <summary>
    /// Inserts several selected fields and tables (every column of a table) into one side, in the given order.
    /// A field selected on its own and through its table is inserted once.
    /// </summary>
    public void Insert(Side side, IReadOnlyList<object> nodes)
    {
        if (nodes.Count == 0)
        {
            _dialogs.ShowInfo(Localized.Format(Strings.Comparisons_SelectInTreeFirst, side));
            return;
        }

        if (Selected is null)
            NewComparison();
        var target = Selected!.FieldsOf(side);

        var candidates = nodes
            .SelectMany(node => node switch
            {
                TableNode table => table.Fields.Where(f => !f.IsMeasure),
                FieldNode field => [field],
                _ => [],
            })
            .DistinctBy(f => f.Ref)
            .ToList();

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
            _dialogs.ShowInfo(candidates.Count == 1 ? Strings.Comparisons_FieldAlreadyIn : Strings.Comparisons_ColumnsAlreadyIn);
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
            _dialogs.ShowInfo(Localized.Format(Strings.Comparisons_SelectInGridFirst, source));
            return;
        }

        if (Selected.MetadataOf(target) is not { } meta)
        {
            _dialogs.ShowInfo(Localized.Format(Strings.Comparisons_ConnectTargetFirst, target));
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
            var message = Localized.Format(Strings.Comparisons_FieldsAdded, added, target) +
                          (already > 0 ? " " + Localized.Format(Strings.Comparisons_FieldsAlreadyThere, already) : "");
            if (missing.Count > 0)
                message += Environment.NewLine + Environment.NewLine + Localized.Format(Strings.Comparisons_NotFoundInModel, target) +
                           Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing);
            _dialogs.ShowInfo(message, Strings.Comparisons_AddSameTitle);
        }
    }

    [RelayCommand]
    private void MoveFieldUp(ComparisonFieldViewModel? field) => Move(field, -1);

    [RelayCommand]
    private void MoveFieldDown(ComparisonFieldViewModel? field) => Move(field, 1);

    /// <summary>
    /// Moves fields (dragged rows) of one side so they sit together, in their current order, before
    /// <paramref name="before"/>, or at the end when it is null. Order matters: pairs and result columns follow model A.
    /// </summary>
    public void MoveFields(IReadOnlyList<ComparisonFieldViewModel> fields, ComparisonFieldViewModel? before)
    {
        if (Selected is null || fields.Count == 0)
            return;
        var list = Selected.FieldsA.Contains(fields[0]) ? Selected.FieldsA : Selected.FieldsB;
        var moving = list.Where(fields.Contains).ToList();
        if (before is not null && moving.Contains(before))
            return;
        var rest = list.Where(f => !moving.Contains(f)).ToList();
        var at = before is null ? rest.Count : rest.IndexOf(before);
        if (at < 0)
            return;
        rest.InsertRange(at, moving);
        // Move in place (not Clear/Add) so selection and the grid's scroll position survive.
        for (var i = 0; i < rest.Count; i++)
        {
            var from = list.IndexOf(rest[i]);
            if (from != i)
                list.Move(from, i);
        }
    }

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
            ? Strings.Comparisons_NoNewMatches
            : Localized.Format(Strings.Comparisons_SynonymsSet, result.Matches.Count) + Environment.NewLine +
              string.Join(Environment.NewLine, result.Matches.Select(m => "  " + Localized.Format(Strings.Comparisons_SynonymPair, m.FieldB, m.Synonym, m.FieldA)));
        if (result.Ambiguous.Count > 0)
            message += Environment.NewLine + Environment.NewLine + Strings.Comparisons_AmbiguousSetByHand + Environment.NewLine + "  " +
                       string.Join(Environment.NewLine + "  ", result.Ambiguous);
        _dialogs.ShowInfo(message, Strings.Comparisons_MatchSynonymsTitle);
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
            _dialogs.ShowInfo(Strings.Comparisons_ConnectBothFirst);
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
                    : Localized.Format(Strings.Comparisons_NoEquivalent, field.Field));
                continue;
            }

            var kind = b.KindOf(right);
            var aggregation = AggregationRules.IsAllowed(field.Aggregation, right.Kind, kind, field.IsKey)
                ? field.Aggregation
                : AggregationRules.Default(right.Kind, kind, field.IsKey);
            Selected.FieldsB.Add(new ComparisonFieldViewModel(right, kind, field.IsKey, aggregation, field.Synonym));
            added.Add(Localized.Format(Strings.Comparisons_AddedAs, right, field.Synonym));
        }

        var message = added.Count == 0
            ? Strings.Comparisons_NoFieldsAdded
            : Localized.Format(Strings.Comparisons_AddedFields, added.Count) + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", added);
        if (unresolved.Count > 0)
            message += Environment.NewLine + Environment.NewLine + Strings.Comparisons_NotMatchedInsertByHand + Environment.NewLine + "  " +
                       string.Join(Environment.NewLine + "  ", unresolved);
        _dialogs.ShowInfo(message, Strings.Comparisons_AddMatchingTitle);
    }
}
