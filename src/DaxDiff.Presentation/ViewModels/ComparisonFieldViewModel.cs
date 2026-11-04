using CommunityToolkit.Mvvm.ComponentModel;
using DaxDiff.Domain;
using DaxDiff.Desktop.Resources;
using DaxDiff.Desktop.Services;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>One inserted field in a comparison: key flag, aggregation and synonym (FR-006, FR-017, FR-018).</summary>
public sealed partial class ComparisonFieldViewModel : ObservableObject
{
    public ComparisonFieldViewModel(FieldRef field, DataKind dataKind, bool isKey, Aggregation aggregation, string synonym)
    {
        Field = field;
        DataKind = dataKind;
        _isKey = isKey;
        _synonym = synonym;
        // A saved aggregation is kept even if the type no longer allows it; validation reports it instead of changing it silently.
        var allowed = AggregationRules.Allowed(field.Kind, dataKind, isKey);
        _allowedAggregations = allowed.Contains(aggregation) ? allowed : [aggregation, .. allowed];
        _aggregation = aggregation;
    }

    public FieldRef Field { get; }
    public DataKind DataKind { get; }
    public bool IsMeasure => Field.Kind == FieldKind.Measure;
    public string FieldText => IsMeasure ? $"Σ {Field.Table}.[{Field.Name}]" : $"{Field.Table}[{Field.Name}]";
    public string TypeText => IsMeasure
        ? Localized.Format(Strings.Field_MeasureType, EnumText.Of(DataKind))
        : EnumText.Of(DataKind);

    /// <summary>Measures are evaluated as defined, so they cannot be keys.</summary>
    public bool CanBeKey => !IsMeasure;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAggregate))]
    private bool _isKey;

    [ObservableProperty]
    private Aggregation _aggregation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAggregate))]
    private IReadOnlyList<Aggregation> _allowedAggregations;

    [ObservableProperty]
    private string _synonym;

    /// <summary>The counterpart in the other model, or why there is none. Set by validation.</summary>
    [ObservableProperty]
    private string? _pairText;

    [ObservableProperty]
    private string? _issue;

    /// <summary>True when <see cref="Issue"/> includes an error that blocks the run (not only warnings).</summary>
    [ObservableProperty]
    private bool _hasError;

    public bool CanAggregate => AllowedAggregations.Count > 1;

    partial void OnIsKeyChanged(bool value)
    {
        AllowedAggregations = AggregationRules.Allowed(Field.Kind, DataKind, value);
        Aggregation = AggregationRules.Default(Field.Kind, DataKind, value);
    }

    public ComparisonField ToModel() => new() { Field = Field, IsKey = IsKey, Aggregation = Aggregation, Synonym = Synonym };

    public static ComparisonFieldViewModel From(ComparisonField field, ModelMetadata? metadata) =>
        new(field.Field, metadata?.KindOf(field.Field) ?? DataKind.Unknown, field.IsKey, field.Aggregation, field.Synonym);

    public static ComparisonFieldViewModel Insert(FieldRef field, DataKind dataKind) =>
        new(field, dataKind, isKey: false, AggregationRules.Default(field.Kind, dataKind, isKey: false), field.Name);
}
