# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

DAXDiff is a Windows WPF desktop app that compares the evaluated output of two Power BI semantic models. A project holds two sources and any number of comparisons. Each comparison picks columns (or measures) on both sides, marks key columns, aggregates the rest, and pairs fields by synonym. DAXDiff evaluates both sides at the key grain, full-outer-joins the results and exports an Excel audit workbook. The spec is `DAXDiff_Requirements.docx` (SRS v1.1). Code comments, tests and `docs/implementation-plan.md` cite its IDs (FR-xxx, NFR-xx, AT-xx, §n), so keep citing them.

## Commands

```powershell
dotnet build                                   # whole solution (DaxDiff.sln)
dotnet test tests/DaxDiff.Tests.Unit           # all unit tests
dotnet test tests/DaxDiff.Tests.Unit --filter "FullyQualifiedName~ComparisonEngineTests"          # one class
dotnet test tests/DaxDiff.Tests.Unit --filter "FullyQualifiedName~ComparisonEngineTests.AT04_AbsoluteTolerance"  # one test
dotnet run --project src/DaxDiff.Desktop       # launch the app
./scripts/build.ps1                            # Release restore/build/test/publish to ./publish
```

There is no linter configured. On the developer's machine, an application-control policy blocks the unsigned `DAXDiff.exe` with "Access is denied". Run the built app as `dotnet <output>/DAXDiff.dll` instead. The desktop output lands in `bin/Debug/net10.0-windows/win-x64/` because a `RuntimeIdentifier` is set.

## Architecture

The dependency direction is Desktop → Application → {Comparison, Connectors, Export} → Domain.

- **Target frameworks:** `Domain`, `Comparison` and `Export` target `net10.0`. `Connectors`, `Application`, `Desktop` and the test project target `net10.0-windows`, because Connectors uses `System.Management` for WMI.
- **Shared build settings:** `Directory.Build.props` sets Nullable, ImplicitUsings and the version for every project.
- **Implicit usings in Desktop:** WPF projects don't get `System.IO` implicitly, so the Desktop csproj adds `<Using Include="System.IO" />`.

### Data model: project → comparisons → compiled pairs

- **Project.** `ProjectFile` (`.pbicomp`) holds `SourceA`, `SourceB` and a list of `ComparisonDefinition`s. Everything the user sets up lives in a project (FR-019); results never do.
- **Comparison.** A `ComparisonDefinition` has `FieldsA` and `FieldsB` (each a `ComparisonField`: field ref, `IsKey`, `Aggregation`, `Synonym`) and its own `ComparisonOptions` (filters, tolerance, normalization, limits).
- **Pairing is by synonym, not explicit pairs.** `ComparisonCompiler.Compile` matches A and B fields whose `SynonymKey` (trimmed, upper-cased) is equal. It emits a `ComparisonConfig`: `FieldPairs` (keys first, then values, in model A order) plus `Options`. Each `FieldPair` carries `LeftAggregation` and `RightAggregation`.
- **One validation entry point.** `ComparisonCompiler.CompileAndValidate` returns the compiler's issues (unpaired keys, duplicate synonyms, disallowed aggregations, missing fields) plus `MappingValidator`'s (roles, type compatibility after aggregation, filters, settings). The UI and `RunRequest.For` both use it.
- **Aggregation rules.** `AggregationRules` (Domain) decides which aggregations a field may use, from `FieldKind`, `DataKind` and `IsKey`. It also gives the default on insert and the aggregated result type. Keys and measures only allow `None`; non-key columns must have an aggregation.

### End-to-end flow of one run (`Application/ComparisonRunner.RunAsync`)

1. `RunRequest.For(connA, connB, definition, metaA, metaB)` compiles and validates. Any error returns a `Failed` result before querying.
2. `DaxQueryBuilder.BuildComparisonQuery(config, Side, metadata)` builds a separate DAX query per side, using that side's field names.
   - It is a `SUMMARIZECOLUMNS` over key columns plus values aliased `"V0"`, `"V1"` and so on.
   - A value is either a measure reference or `ValueExpression(column, aggregation)`, which emits `SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNTA`, `DISTINCTCOUNT` or `CONCATENATEX(VALUES(...))`.
   - Filters become `DEFINE VAR` TREATAS/FILTER expressions.
   - A keys-only comparison (no values) is allowed.
3. Both sides run in parallel through `IModelConnector.ExecuteDaxAsync`, with a per-attempt timeout and retry on `DaxDiffException.IsTransient`.
4. `ComparisonEngine.Compare(resultA, resultB, config)` reads result columns **by ordinal**: keys first, then values. Column names are ignored, because REST and ADOMD name them differently.
5. Status is decided as Complete, Partial (truncated, or duplicates under the Diagnose policy), Failed (errors, or duplicates under the default Fail policy) or Cancelled. Expected failures come back as a `ComparisonResult` carrying the `ComparisonName`, never as an exception.

### Invariants the code relies on (don't break them)

- **No silent truncation (NFR-05).** Connectors stop reading at `MaxRows` and set `QueryResult.IsTruncated`. `ServiceRestConnector.Parse` also flags truncation when the result reaches the REST limits (100k rows or 1M values). A truncated result must never produce `RunStatus.Complete`.
- **Key canonicalization lives in `ValueCanonicalizer`.**
  - Composite keys are length-prefixed per part so separators can't collide.
  - Text is never cast to a number, so "0012" ≠ 12.
  - Numbers go through decimal and then `Normalize`, so 1.20 == 1.2 and `long` 2025 == `decimal` 2025.
  - Normalization (trim, case, Unicode, blank==empty, date-only) only happens when `KeyNormalization` asks for it.
- **Duplicate normalized keys** become `RowStatus.InvalidKey` rows and are excluded from matching. They are never deduplicated.
- **REST must match ADOMD types.** `ServiceRestConnector.ToClr` turns JSON numbers into `long` or `decimal` and ISO timestamp strings into `DateTime`, so keys compare equal across transports.
- **Safe DAX (FR-009).** All identifiers go through `TableRef`/`ColumnRef`/`MeasureRef`, which escape `'` and `]`. Filter values go through `TypedLiteral`, which parses them by column `DataKind` and throws `FormatException` otherwise. Never splice user text into DAX.
- **Projects (`.pbicomp`, `Application/ProjectStore`).**
  - JSON is camelCase with string enums and a `schemaVersion` field (currently 2). `Migrate()` upgrades older versions: v1's single `config.fieldPairs` becomes one comparison whose fields share each pair's alias as their synonym.
  - Any breaking JSON change needs a `CurrentSchemaVersion` bump and a migration step.
  - `Serialize` and `Fingerprint` strip `DesktopServer` and `DesktopDatabase`, because those are session-specific ports and IDs. `Fingerprint` leaves out the save time and drives the unsaved-changes check.
  - Computed properties on domain records need `[JsonIgnore]`, as on `ComparisonConfig.Keys` and `FieldRef.QualifiedName`.
- **Saved aggregations are never changed silently.** `ComparisonFieldViewModel` keeps a loaded aggregation even when the type (possibly not known yet) disallows it. Validation reports `CMP-AGGREGATION` instead.
- **Excel export (`Export/ExcelReportWriter`).**
  - `SetValue` writes native types.
  - Strings starting with `= + - @` get `IncludeQuotePrefix` (formula-injection guard, AT-12).
  - Detail sheets split at `MaxRowsPerSheet` (AT-11).
  - DAX text is omitted unless `IncludeQueryText` is set.

### Connectors (`DaxDiff.Connectors`)

- **Shared ADOMD base.** `AdomdConnectorBase` is used by `DesktopSessionConnector` (`localhost:<port>`) and `ServiceXmlaConnector` (`powerbi://…/<workspace>`). It serializes commands with a semaphore and wraps synchronous ADOMD calls in `Task.Run`. Cancellation goes through `command.Cancel()`.
- **Metadata fallback chain.** Discovery tries `TMSCHEMA_*` DMVs first (admin), then `INFO.VIEW.*()` DAX, then `MDSCHEMA`/`DBSCHEMA` rowsets (partial). Parsers are in `MetadataReaders.cs`; these classes are `internal`, with `InternalsVisibleTo` set for the test project.
- **XMLA auth.** XMLA takes an MSAL token through `AdomdConnection.AccessToken` when `AuthOptions.ClientId` is set. Otherwise ADOMD's built-in interactive sign-in is used. REST and workspace discovery (`PowerBiRestClient`, `PowerBiDiscoveryService`) require a client ID.
- **Session discovery.** `DesktopSessionLocator` finds Power BI Desktop sessions through WMI: `msmdsrv.exe` processes, their `-s <dataDir>` argument, the UTF-16 `msmdsrv.port.txt` file, and the parent `PBIDesktop.exe` process. If WMI fails it falls back to scanning the workspace folders.
- **Errors.** All errors become `DaxDiffException` (category, diagnostic code, remedy, side, transient flag) through `ErrorClassifier`. Its message heuristics are covered by tests in `ConnectorParsingTests`.

### Desktop (`DaxDiff.Desktop`, MVVM with CommunityToolkit.Mvvm)

- **Startup.** `App.OnStartup` builds a Generic Host with Serilog file logging to `%LOCALAPPDATA%\DAXDiff\logs`. It reads `appsettings.json` plus the optional user override `%LOCALAPPDATA%\DAXDiff\settings.json`, then shows `MainWindow` and applies the External Tools arguments (`--server` and `--database`, or positional `localhost:port`). The attach waits until a project is created or opened.
- **Project lifecycle.** `MainViewModel` owns it: New project creates the file first, then Open, Save, Close, and an unsaved-changes prompt (also on window close). The project steps in the nav are disabled until a project is open.
- **View models.** `MainViewModel` owns two `SourcePanelViewModel`s (A and B) and the page view models.
  - `ComparisonsViewModel` holds one `ComparisonEditorViewModel` per comparison.
  - Each editor owns its `FieldsA`/`FieldsB` (`ComparisonFieldViewModel`) and its own `OptionsViewModel`, and recompiles on every change. `Recompile()` annotates each field with its counterpart and its issues.
  - The Options page (`OptionsPageViewModel`) shows the selected comparison's options. Run executes the ticked comparisons one after another. Results keeps the latest result per comparison.
- **Pages.** Each page is mapped to a view by `DataTemplate`s in `MainWindow.xaml`. The results grid builds its columns in code (`ResultsView.xaml.cs`) and binds to `ResultRowView.Cells[i]`. Sorting uses a custom comparer (`ResultsViewModel.Sort`).
- **Typed command parameters.** A `CommandParameter` set in XAML arrives as a string, so a `RelayCommand<bool?>` throws. Use separate commands or a `string` parameter.
- **Rendering.** Views reference styles and converters defined in `App.xaml` with `StaticResource`, so rendering a view outside the running app requires `new App().InitializeComponent()` first.

## Testing

xUnit tests are in `tests/DaxDiff.Tests.Unit`. `Fixtures/Fixture.cs` builds synthetic metadata, configs and `QueryResult`s. Its helpers are `Key(...)`, `Value(...)`, `Field(...)`, and `config.With(o => o with { ... })` for changing options. `Fixtures/FakeConnector.cs` is an in-memory `IModelConnector`; use it for orchestration tests instead of real models. Test names are prefixed with the requirement or acceptance-test ID they cover, such as `AT07_…` or `FR018_…`. The Desktop project (a WinExe) has no tests; to exercise its view models, use an offscreen harness.

Nothing has been verified against live Power BI models yet; this is Stage 0 in `docs/implementation-plan.md`. That covers Desktop attach, XMLA, REST and the DMV/INFO.VIEW queries, so treat connector behaviour against real services as unconfirmed.
