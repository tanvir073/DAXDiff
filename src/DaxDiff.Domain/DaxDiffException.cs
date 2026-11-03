using System.Globalization;
using DaxDiff.Domain.Resources;
namespace DaxDiff.Domain;

public enum ErrorCategory
{
    Network,
    Authentication,
    Authorization,
    UnsupportedModel,
    InvalidQuery,
    Timeout,
    Throttled,
    Export,
    Configuration,
    Unknown,
}

/// <summary>
/// A user-facing failure with the source side, a remedial action and a support diagnostic code (FR-015).
/// </summary>
public sealed class DaxDiffException : Exception
{
    public DaxDiffException(
        ErrorCategory category,
        string diagnosticCode,
        string message,
        string remedy,
        Side? side = null,
        bool isTransient = false,
        Exception? inner = null)
        : base(message, inner)
    {
        Category = category;
        DiagnosticCode = diagnosticCode;
        Remedy = remedy;
        Side = side;
        IsTransient = isTransient;
    }

    public ErrorCategory Category { get; }
    public string DiagnosticCode { get; }
    public string Remedy { get; }
    public Side? Side { get; private set; }
    public bool IsTransient { get; }

    public DaxDiffException WithSide(Side side)
    {
        Side ??= side;
        return this;
    }

    public string ToUserText() =>
        (Side is { } s ? string.Format(CultureInfo.CurrentCulture, Strings.Error_ModelPrefix, s) : "") + Message + Environment.NewLine +
        string.Format(CultureInfo.CurrentCulture, Strings.Error_WhatToDo, Remedy) + Environment.NewLine +
        string.Format(CultureInfo.CurrentCulture, Strings.Error_DiagnosticCode, DiagnosticCode);
}
