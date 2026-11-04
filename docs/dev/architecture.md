# Architecture

## Projects

```text
App (Avalonia) ─┐
Desktop (WPF) ──┴► Presentation ──► Application ──► Comparison ──────► Domain
                       │               ├────────► Export ──────────► Domain
                       │               └────────► Connectors.Core ─► Domain
                       └─────────► Connectors ──► Connectors.Core
Web ────────────────────────────► Application
```

| Project | Target | Responsibility |
|---|---|---|
| `DaxDiff.Domain` | net10.0 | Contracts: model references, metadata, comparison definitions, aggregation rules, config, results, project file |
| `DaxDiff.Comparison` | net10.0 | Synonym compiler, DAX query builder, key canonicalization, comparison engine, auto-matcher, validator |
| `DaxDiff.Export` | net10.0 | Excel audit workbook (ClosedXML) |
| `DaxDiff.Connectors.Core` | net10.0 | `IModelConnector`, auth abstractions, error classification, metadata parsers, the REST connector and discovery |
| `DaxDiff.Connectors` | net10.0 | ADOMD.NET (Desktop session, XMLA), WMI session discovery (Windows only), MSAL with the encrypted token cache (DPAPI, Keychain or keyring), `ConnectorFactory` |
| `DaxDiff.Application` | net10.0 | Run orchestration (parallel, retries, timeouts), project persistence and migration, product links |
| `DaxDiff.Presentation` | net10.0 | View models (CommunityToolkit.Mvvm), their services and the desktop text, shared by the desktop apps; no UI toolkit |
| `DaxDiff.App` | net10.0 | Cross-platform desktop app (Avalonia 12): views, dialogs, theme brushes. Windows, macOS, Linux |
| `DaxDiff.Desktop` | net10.0-windows | WPF views, dialogs, theme brushes. Windows only; retired once `DaxDiff.App` matches it |
| `DaxDiff.Web` | net10.0 | Blazor WebAssembly app, MSAL.js, REST only |
| `DaxDiff.Tests.Unit` | net10.0 | xUnit tests, run on Windows, Linux and macOS |

Native-client code (ADOMD, MSAL.NET, WMI) stays in `Connectors` and the desktop apps, so the web app keeps building.
Windows-only calls are guarded with `OperatingSystem.IsWindows()`. CI builds and tests on Windows, Linux and macOS.

`Directory.Build.props` sets Nullable, ImplicitUsings, the version and the package metadata for every project.

## Data model

- **Project** (`ProjectFile`, saved as `.pbicomp`): `SourceA`, `SourceB` and a list of `ComparisonDefinition`s. A
  project holds everything the user sets up (FR-019), never results.
- **Comparison** (`ComparisonDefinition`): `FieldsA` and `FieldsB`, each a `ComparisonField` (field ref, `IsKey`,
  `Aggregation`, `Synonym`), plus its own `ComparisonOptions` (filters, tolerance, normalization, limits, result rows).
- **Pairing by synonym.** `ComparisonCompiler.Compile` pairs A and B fields whose trimmed, upper-cased synonyms are
  equal and emits a `ComparisonConfig`: `FieldPairs` (keys first, then values, in model A order) plus `Options`.
- **One validation entry point.** `ComparisonCompiler.CompileAndValidate` returns the compiler's issues plus
  `MappingValidator`'s. The UI and `RunRequest.For` both use it.
- **Aggregation rules.** `AggregationRules` decides which aggregations a field allows and the aggregated result type.

## One run (`ComparisonRunner.RunAsync`)

1. `RunRequest.For` compiles and validates. Any error returns a `Failed` result before querying.
2. `DaxQueryBuilder.BuildComparisonQuery` builds one DAX query per side: `SUMMARIZECOLUMNS` over the keys plus values
   aliased `V0`, `V1`… Filters become `DEFINE VAR` TREATAS/FILTER expressions.
3. Both sides run in parallel through `IModelConnector.ExecuteDaxAsync`, with a per-attempt timeout and retries on
   transient errors.
4. `ComparisonEngine.Compare` reads result columns **by ordinal** (REST and ADOMD name them differently), canonicalizes
   keys, full-outer-joins and classifies every row.
5. The status is Complete, Partial, Failed or Cancelled. Expected failures are returned as a `ComparisonResult`, never
   thrown.

## Invariants

- **No silent truncation (NFR-05).** Connectors stop at `MaxRows` and set `IsTruncated`; REST also flags results that
  reach its limits. A truncated result is never `Complete`.
- **Key canonicalization** lives in `ValueCanonicalizer`. Composite keys are length-prefixed; text is never cast to a
  number; numbers are normalized decimals. Normalization happens only when the options ask for it.
- **Duplicate normalized keys** become `InvalidKey` rows. They are never deduplicated.
- **Result settings only trim the listing.** Counts always cover every row.
- **Safe DAX (FR-009).** Identifiers go through `TableRef`/`ColumnRef`/`MeasureRef`; filter values through
  `TypedLiteral`. User text is never spliced into DAX.
- **Project schema.** JSON is camelCase with string enums and `schemaVersion` (currently 2). Any breaking change needs a
  `CurrentSchemaVersion` bump and a step in `ProjectStore.Migrate`. Session-specific Desktop ports are not saved.
- **Excel export.** Native cell types; text starting with `= + - @` is quote-prefixed (AT-12); detail sheets split at
  `MaxRowsPerSheet` (AT-11); DAX text only when asked for.

## Connectors

- `AdomdConnectorBase` (Desktop session on `localhost:<port>`, XMLA on `powerbi://…`) serializes commands and cancels
  through `command.Cancel()`.
- Metadata discovery tries `TMSCHEMA_*` DMVs, then `INFO.VIEW.*()`, then `MDSCHEMA`/`DBSCHEMA` rowsets.
- Sign-in is per source (FR-002): each source has its own `SourceSession` with its own tenant and account. All MSAL
  instances share one encrypted cache file.
- `DesktopSessionLocator` finds Power BI Desktop sessions through WMI, with a folder scan as fallback.
- Every error becomes a `DaxDiffException` with a category, diagnostic code, remedy, side and transient flag.

## Desktop

- `App.OnStartup` builds a Generic Host with Serilog file logging, reads `appsettings.json` and the user's
  `settings.json`, and applies the External Tools arguments.
- `MainViewModel` owns the project lifecycle. Comparisons are drafts until saved (FR-021): `ComparisonEditorViewModel`
  keeps a `SavedDefinition`, which is what runs and what is written.
- Theme: `Themes/Numberskills.xaml` and `ThemeService` (10 colour themes). Reference brushes with `DynamicResource`.
- Language: English and Swedish. All user-facing text is in `Resources/*.resx`; `LocalizationTests` fails on a
  missing Swedish key or mismatched placeholders.

## Web

A static Blazor WebAssembly site. `ProjectState` holds the open project and two `SourceState`s, each with its own
`BrowserAuthService`. Only REST sources work in the browser. See [web deployment](web-deployment.md).

## Status

Nothing has been verified against live Power BI models yet (Stage 0 in the
[implementation plan](implementation-plan.md)). Treat connector behaviour against real services as unconfirmed.
