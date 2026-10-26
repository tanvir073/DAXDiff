using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>One row of the pairing table.</summary>
public sealed partial class PairRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleText))]
    private FieldRole _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftText))]
    private FieldRef _left = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightText))]
    private FieldRef _right = null!;

    [ObservableProperty]
    private string _alias = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmText))]
    private bool _isConfirmed = true;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string? _issue;

    public string RoleText => Role == FieldRole.Dimension ? "Dimension (key)" : "Measure";
    public string LeftText => Left.ToString();
    public string RightText => Right.ToString();
    public string ConfirmText => IsConfirmed ? "Accepted" : "Suggested – accept to use";

    public FieldPair ToPair() => new() { Left = Left, Right = Right, Role = Role, Alias = Alias.Trim(), IsConfirmed = IsConfirmed };

    public static PairRowViewModel From(FieldPair pair, string? note = null) => new()
    {
        Role = pair.Role,
        Left = pair.Left,
        Right = pair.Right,
        Alias = pair.Alias,
        IsConfirmed = pair.IsConfirmed,
        Note = note,
    };
}

/// <summary>Field pairing: dual searchable trees, pair table, suggestions and repair (FR-006, FR-007, AT-14).</summary>
public sealed partial class MappingViewModel : ObservableObject
{
    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private readonly AutoMatcher _matcher;
    private readonly IDialogService _dialogs;

    public MappingViewModel(SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, AutoMatcher matcher, IDialogService dialogs)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        _matcher = matcher;
        _dialogs = dialogs;
        _sourceA.MetadataChanged += (_, _) => RebuildTrees();
        _sourceB.MetadataChanged += (_, _) => RebuildTrees();
        Pairs.CollectionChanged += (_, e) =>
        {
            if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Add or System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                foreach (PairRowViewModel row in e.NewItems!)
                    row.PropertyChanged += OnRowChanged;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised whenever pairs change so the shell can revalidate.</summary>
    public event EventHandler? Changed;

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Issue text is set by validation itself; reacting to it would loop.
        if (e.PropertyName != nameof(PairRowViewModel.Issue))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public ObservableCollection<PairRowViewModel> Pairs { get; } = [];

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

    [ObservableProperty]
    private FieldNode? _selectedA;

    [ObservableProperty]
    private FieldNode? _selectedB;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePairCommand), nameof(AcceptPairCommand), nameof(ReplaceLeftCommand), nameof(ReplaceRightCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private PairRowViewModel? _selectedPair;

    [ObservableProperty]
    private IReadOnlyList<ValidationIssue> _issues = [];

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

    public IReadOnlyList<FieldPair> ToPairs() => Pairs.Select(p => p.ToPair()).ToList();

    public void Load(IEnumerable<FieldPair> pairs)
    {
        Pairs.Clear();
        foreach (var pair in pairs)
            Pairs.Add(PairRowViewModel.From(pair));
    }

    /// <summary>Marks rows with their validation problems (e.g. a renamed field) so they can be repaired.</summary>
    public void ApplyIssues(IReadOnlyList<ValidationIssue> issues)
    {
        Issues = issues;
        foreach (var row in Pairs)
        {
            var mine = issues.Where(i => i.Alias is not null && string.Equals(i.Alias, row.Alias, StringComparison.Ordinal)).Select(i => i.Message).ToList();
            row.Issue = mine.Count == 0 ? null : string.Join(" ", mine);
        }
    }

    [RelayCommand]
    private void AddPair()
    {
        if (SelectedA is null || SelectedB is null)
        {
            _dialogs.ShowInfo("Select a field in the Model A tree and a field in the Model B tree, then add the pair.");
            return;
        }

        if (SelectedA.IsMeasure != SelectedB.IsMeasure)
        {
            _dialogs.ShowInfo("Pair a column with a column (dimension key) or a measure with a measure.");
            return;
        }

        Pairs.Add(new PairRowViewModel
        {
            Role = SelectedA.IsMeasure ? FieldRole.Measure : FieldRole.Dimension,
            Left = SelectedA.Ref,
            Right = SelectedB.Ref,
            Alias = UniqueAlias(SelectedA.Ref.Name),
        });
    }

    [RelayCommand]
    private void SuggestMeasures()
    {
        if (_sourceA.Metadata is not { } a || _sourceB.Metadata is not { } b)
        {
            _dialogs.ShowInfo("Connect both models first.");
            return;
        }

        var mapped = Pairs.Where(p => p.Role == FieldRole.Measure).Select(p => p.Left).ToHashSet();
        var added = 0;
        var ambiguous = new List<string>();
        foreach (var suggestion in _matcher.SuggestMeasures(a, b).Where(s => !mapped.Contains(s.Left)))
        {
            if (suggestion.ToUnconfirmedPair() is { } pair)
            {
                Pairs.Add(PairRowViewModel.From(pair with { Alias = UniqueAlias(pair.Alias) }, suggestion.Reason));
                added++;
            }
            else if (suggestion.IsAmbiguous)
            {
                ambiguous.Add($"{suggestion.Left.Name}: {string.Join(", ", suggestion.Candidates.Select(c => c.Name))}");
            }
        }

        var message = $"{added} measure pair(s) suggested. Review and accept them before running.";
        if (ambiguous.Count > 0)
            message += Environment.NewLine + Environment.NewLine + "Ambiguous (map manually):" + Environment.NewLine + string.Join(Environment.NewLine, ambiguous.Take(20));
        _dialogs.ShowInfo(message, "Suggestions");
    }

    [RelayCommand]
    private void SuggestForSelected()
    {
        if (SelectedA is null || _sourceA.Metadata is not { } a || _sourceB.Metadata is not { } b)
        {
            _dialogs.ShowInfo("Select a Model A field and connect both models first.");
            return;
        }

        var suggestion = SelectedA.IsMeasure ? _matcher.SuggestMeasure(SelectedA.Ref, b) : _matcher.SuggestColumn(SelectedA.Ref, a, b);
        if (suggestion.ToUnconfirmedPair() is { } pair)
        {
            Pairs.Add(PairRowViewModel.From(pair with { Alias = UniqueAlias(pair.Alias) }, suggestion.Reason));
            return;
        }

        _dialogs.ShowInfo(suggestion.IsAmbiguous
            ? $"Several candidates match {SelectedA.Ref.Name}: {string.Join(", ", suggestion.Candidates)}. Select the right one in Model B and add the pair manually."
            : $"No candidate found for {SelectedA.Ref.Name}. Select the equivalent field in Model B and add the pair manually.");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void RemovePair()
    {
        if (SelectedPair is not null)
            Pairs.Remove(SelectedPair);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void AcceptPair()
    {
        if (SelectedPair is not null)
            SelectedPair.IsConfirmed = true;
    }

    [RelayCommand]
    private void AcceptAll()
    {
        foreach (var pair in Pairs)
            pair.IsConfirmed = true;
    }

    [RelayCommand]
    private void ClearPairs()
    {
        if (Pairs.Count > 0 && _dialogs.Confirm("Remove all pairs?"))
            Pairs.Clear();
    }

    /// <summary>Repair: point the selected pair's A side at the field selected in the A tree.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void ReplaceLeft()
    {
        if (SelectedPair is null || SelectedA is null)
            return;
        if (SelectedA.IsMeasure != (SelectedPair.Role == FieldRole.Measure))
        {
            _dialogs.ShowInfo("The replacement must be the same kind of field (column or measure).");
            return;
        }

        SelectedPair.Left = SelectedA.Ref;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void ReplaceRight()
    {
        if (SelectedPair is null || SelectedB is null)
            return;
        if (SelectedB.IsMeasure != (SelectedPair.Role == FieldRole.Measure))
        {
            _dialogs.ShowInfo("The replacement must be the same kind of field (column or measure).");
            return;
        }

        SelectedPair.Right = SelectedB.Ref;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedPair))]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedPair is null)
            return;
        var index = Pairs.IndexOf(SelectedPair);
        var target = index + delta;
        if (target < 0 || target >= Pairs.Count)
            return;
        var pair = SelectedPair;
        Pairs.Move(index, target);
        SelectedPair = pair;
    }

    private bool HasSelectedPair() => SelectedPair is not null;

    private string UniqueAlias(string baseAlias)
    {
        var alias = baseAlias;
        for (var i = 2; Pairs.Any(p => string.Equals(p.Alias.Trim(), alias, StringComparison.OrdinalIgnoreCase)); i++)
            alias = $"{baseAlias} {i}";
        return alias;
    }
}
