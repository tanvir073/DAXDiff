# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

DAXDiff is a Windows WPF desktop app that compares the evaluated output of two Power BI semantic models. A project holds two sources and any number of comparisons. Each comparison picks columns (or measures) on both sides, marks key columns, aggregates the rest, and pairs fields by synonym. DAXDiff evaluates both sides at the key grain, full-outer-joins the results and exports an Excel audit workbook. The spec is `DAXDiff_Requirements.docx` (SRS v1.2). Code comments, tests and `docs/implementation-plan.md` cite its IDs (FR-xxx, NFR-xx, AT-xx, §n), so keep citing them.

## Commands

```powershell
dotnet build                                   # whole solution (DaxDiff.sln)
dotnet test tests/DaxDiff.Tests.Unit           # all unit tests
dotnet test tests/DaxDiff.Tests.Unit --filter "FullyQualifiedName~ComparisonEngineTests"          # one class
dotnet test tests/DaxDiff.Tests.Unit --filter "FullyQualifiedName~ComparisonEngineTests.AT04_AbsoluteTolerance"  # one test
dotnet run --project src/DaxDiff.Desktop       # launch the app
dotnet run --project src/DaxDiff.Web --launch-profile http   # web app on http://localhost:5215
./scripts/build.ps1                            # Release restore/build/test/publish to ./publish
./scripts/package.ps1                          # self-contained publish + MSI (WiX 5, installer/) to ./dist
```

There is no linter configured. On the developer's machine, an application-control policy blocks the unsigned `DAXDiff.exe` with "Access is denied". Run the built app as `dotnet <output>/DAXDiff.dll` instead. The desktop output lands in `bin/Debug/net10.0-windows/win-x64/` because a `RuntimeIdentifier` is set.

## Architecture

The dependency direction is Desktop → {Application, Connectors} and Web → Application → {Comparison, Connectors.Core, Export} → Domain, with Connectors → Connectors.Core.

- **Target frameworks:** `Domain`, `Comparison`, `Export`, `Connectors.Core`, `Application` and `Web` target `net10.0`. `Connectors`, `Desktop` and the test project target `net10.0-windows`, because Connectors uses `System.Management` for WMI and ADOMD.
- **Connectors.Core vs Connectors.** `Connectors.Core` holds everything without a Windows dependency: `IModelConnector`, `IAuthService`/`AuthOptions`/`SourceSession`, `ErrorClassifier`, the metadata parsers and the REST connector, client and discovery. `Connectors` holds ADOMD (Desktop, XMLA), WMI session discovery, `MsalAuthService` (which maps `MsalException` itself) and `ConnectorFactory`. Namespaces did not change. Keep Windows-only code out of Core and Application, or the web app stops building.
- **Shared build settings:** `Directory.Build.props` sets Nullable, ImplicitUsings and the version for every project.
- **Implicit usings in Desktop:** WPF projects don't get `System.IO` implicitly, so the Desktop csproj adds `<Using Include="System.IO" />`.

### Data model: project → comparisons → compiled pairs

- **Project.** `ProjectFile` (`.pbicomp`) holds `SourceA`, `SourceB` and a list of `ComparisonDefinition`s. Everything the user sets up lives in a project (FR-019); results never do.
- **Comparison.** A `ComparisonDefinition` has `FieldsA` and `FieldsB` (each a `ComparisonField`: field ref, `IsKey`, `Aggregation`, `Synonym`) and its own `ComparisonOptions` (filters, tolerance, normalization, limits).
- **Pairing is by synonym, not explicit pairs.** `ComparisonCompiler.Compile` matches A and B fields whose `SynonymKey` (trimmed, upper-cased) is equal. It emits a `ComparisonConfig`: `FieldPairs` (keys first, then values, in model A order) plus `Options`. Each `FieldPair` carries `LeftAggregation` and `RightAggregation`.
- **One validation entry point.** `ComparisonCompiler.CompileAndValidate` returns the compiler's issues (unpaired keys, duplicate synonyms, disallowed aggregations, missing fields) plus `MappingValidator`'s (roles, type compatibility after aggregation, filters, settings). The UI and `RunRequest.For` both use it.
- **Aggregation rules.** `AggregationRules` (Domain) decides which aggregations a field may use, from `FieldKind`, `DataKind` and `IsKey`. It also gives the aggregated result type. `AggregationRules.Default` gives an inserted non-key column Sum (numbers), Concatenate (text) or Max (true/false, "any TRUE"); dates, unknown types, keys and measures start at `None` (FR-017). Keys and measures only allow `None`. Non-key columns list `None` too, but a compared column left at `None` fails validation (`MAP-AGG`) until the user picks one. DAX `MIN`/`MAX` reject booleans, so `DaxQueryBuilder.ValueExpression` evaluates boolean Min/Max through 1/0 and needs the column's `DataKind`.

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
- **Result settings only trim the listing.** `ComparisonOptions.Results` (all rows or differences only, optional row limit; defaults keep every row) is applied by `ComparisonEngine.KeepRows` after the summary is computed, so counts always cover every row. A limit keeps differences before matches. Left-out rows are counted in `ComparisonSummary.RowsOmitted` and reported as a warning; they never change the run status.
- **Duplicate normalized keys** become `RowStatus.InvalidKey` rows and are excluded from matching. They are never deduplicated.
- **REST must match ADOMD types.** `ServiceRestConnector.ToClr` turns JSON numbers into `long` or `decimal` and ISO timestamp strings into `DateTime`, so keys compare equal across transports.
- **Safe DAX (FR-009).** All identifiers go through `TableRef`/`ColumnRef`/`MeasureRef`, which escape `'` and `]`. Filter values go through `TypedLiteral`, which parses them by column `DataKind` and throws `FormatException` otherwise. Never splice user text into DAX.
- **Projects (`.pbicomp`, `Application/ProjectStore`).**
  - JSON is camelCase with string enums and a `schemaVersion` field (currently 2). `Migrate()` upgrades older versions: v1's single `config.fieldPairs` becomes one comparison whose fields share each pair's alias as their synonym.
  - Any breaking JSON change needs a `CurrentSchemaVersion` bump and a migration step.
  - `Serialize` and `Fingerprint` strip `DesktopServer` and `DesktopDatabase`, because those are session-specific ports and IDs. `Fingerprint` leaves out the save time and drives the unsaved-changes check.
  - Computed properties on domain records need `[JsonIgnore]`, as on `ComparisonConfig.Keys` and `FieldRef.QualifiedName`.
- **Filters (FR-020).** A `FilterPair` is either Basic (`Values`, `IncludeBlank`, `In`/`NotIn`) or Advanced (one or two `FilterCondition`s joined by `And`/`Or`).
  - `DaxQueryBuilder.FilterExpression` emits `TREATAS` or `FILTER(ALL(col), …)`. Equality uses strict `==`, so BLANK is never 0. Text conditions use `CONTAINSSTRING`/`LEFT`, with `FORMAT` on non-text columns.
  - `BuildDistinctValuesQuery` feeds the pop-up's value list.
  - `MappingValidator.ValidateFilter` is public so the pop-up can show problems as the user edits.
- **Saved aggregations are never changed silently.** `ComparisonFieldViewModel` keeps a loaded aggregation even when the type (possibly not known yet) disallows it. Validation reports `CMP-AGGREGATION` instead.
- **Excel export (`Export/ExcelReportWriter`).**
  - `SetValue` writes native types.
  - Strings starting with `= + - @` get `IncludeQuotePrefix` (formula-injection guard, AT-12).
  - Detail sheets split at `MaxRowsPerSheet` (AT-11).
  - DAX text is omitted unless `IncludeQueryText` is set.

### Connectors (`DaxDiff.Connectors`)

- **Shared ADOMD base.** `AdomdConnectorBase` is used by `DesktopSessionConnector` (`localhost:<port>`) and `ServiceXmlaConnector` (`powerbi://…/<workspace>`). It serializes commands with a semaphore and wraps synchronous ADOMD calls in `Task.Run`. Cancellation goes through `command.Cancel()`.
- **Metadata fallback chain.** Discovery tries `TMSCHEMA_*` DMVs first (admin), then `INFO.VIEW.*()` DAX, then `MDSCHEMA`/`DBSCHEMA` rowsets (partial). Parsers are in `MetadataReaders.cs`; these classes are `internal`, with `InternalsVisibleTo` set for the test project.
- **Sign-in is per source (FR-002).** `SourceSessionFactory.Create()` gives each source panel its own `SourceSession`: a `MsalAuthService` with its own `Tenant` and account, plus its own REST client and discovery. Model A and model B can therefore be in different tenants or use different users.
  - All `MsalAuthService` instances share one DPAPI-encrypted cache file through a static `MsalCacheHelper`. Each instance only ever uses its own account (by home account ID, else the remembered `AccountHint`).
  - `IConnectorFactory.Create(reference, session)` needs the source's session.
  - XMLA takes the MSAL token through `AdomdConnection.AccessToken` when `AuthOptions.ClientId` is set; otherwise ADOMD's built-in interactive sign-in is used. `ServiceXmlaConnector.DataSource` replaces `myorg` with the source's tenant for external tenants.
  - REST and workspace discovery require a client ID.
  - `ModelReference.TenantId` and `AccountHint` are saved in projects; tokens never are.
- **Session discovery.** `DesktopSessionLocator` finds Power BI Desktop sessions through WMI: `msmdsrv.exe` processes, their `-s <dataDir>` argument, the UTF-16 `msmdsrv.port.txt` file, and the parent `PBIDesktop.exe` process. If WMI fails it falls back to scanning the workspace folders.
- **Errors.** All errors become `DaxDiffException` (category, diagnostic code, remedy, side, transient flag) through `ErrorClassifier`. Its message heuristics are covered by tests in `ConnectorParsingTests`.

### Desktop (`DaxDiff.Desktop`, MVVM with CommunityToolkit.Mvvm)

- **Startup.** `App.OnStartup` builds a Generic Host with Serilog file logging to `%LOCALAPPDATA%\DAXDiff\logs`. It reads `appsettings.json` plus the optional user override `%LOCALAPPDATA%\DAXDiff\settings.json`, then shows `MainWindow` and applies the External Tools arguments (`--server` and `--database`, or positional `localhost:port`). The attach waits until a project is created or opened.
- **Project lifecycle.** `MainViewModel` owns it: New project creates the file first, then Open, Save, Close, and an unsaved-changes prompt (also on window close). The project steps in the nav are disabled until a project is open.
- **Comparisons are drafts until saved (FR-021).**
  - `ComparisonEditorViewModel` keeps `SavedDefinition`, the version that runs and is written to the project. The editor's live state is a draft.
  - `IsModified` compares JSON fingerprints of the two. `Save` commits the draft and raises `SaveRequested`, which makes the shell write the project file. `Commit()` commits without writing.
  - `ProjectFile.Comparisons` and the Run page use only `SavedDefinition`s. A never-saved comparison cannot run.
  - Project Save and close offer to commit drafts. Deleting a saved comparison also writes the file.
- **View models.** `MainViewModel` owns two `SourcePanelViewModel`s (A and B) and the page view models.
  - `ComparisonsViewModel` holds one `ComparisonEditorViewModel` per comparison.
  - Each editor owns its `FieldsA`/`FieldsB` (`ComparisonFieldViewModel`) and its own `OptionsViewModel`, and recompiles on every change. `Recompile()` annotates each field with its counterpart and its issues.
  - The Options page (`OptionsPageViewModel`) shows the selected comparison's options. Run executes the ticked comparisons one after another, in their saved versions. Results keeps the latest result per comparison.
  - Filters are edited in `FilterEditorWindow` (`FilterEditorViewModel` / `FilterItemViewModel`), opened by `IFilterEditorService` through `OptionsViewModel.EditFiltersRequested`. The value list is loaded from both connected models (first 1,000 values each, searchable).
  - "Add same to B/A" (`ComparisonsViewModel.CopyToB/CopyToA`) copies the selected grid rows to the other model by exact table and column name (measures by name).
- **Theme.** `Themes/Numberskills.xaml` (merged in `App.xaml`) implements the Numberskills design system: the embedded fonts (`Fonts/`, Bricolage Grotesque for headings and Onest for text) and implicit styles for every standard control.
  - **Colours are user-selectable.** `Services/ThemeService` holds 10 colour themes (tokens from the design canvas; Midnight is the default, and an unknown saved id falls back to it) and builds the brush dictionary that replaces the first merged dictionary of `App.xaml` at run time. The user picks a theme on Settings → Appearance; it is saved as `Appearance:Theme` in the user settings file.
  - Always reference brushes with `DynamicResource` (a `StaticResource` brush never changes with the theme and fails if no theme is loaded yet). Text on a fill uses the fill's pair: `ChipBrush`/`ChipTextBrush` for selected states, `AccentFillBrush`/`OnAccentBrush`, `HeroBrush`/`HeroTextBrush`, `MintBrush`/`SelectionTextBrush`, `AccentSoftBrush`/`TileTextBrush`, and the `Header*` brushes on the header. Never hard-code White for text or surfaces, because some themes are dark.
  - Use its keyed styles rather than local colours: `PrimaryButton` (one main action per page), `SecondaryButton`, `LightButton` (on the teal header), `Card`, `CardTitle`, `Eyebrow`/`PageTitle`/`PageHint` for page headers, `Badge` tones, `Segment`/`SegmentGroup`, `SideChipA`/`SideChipB`, `Message` tones and `CodeBox`.
  - A TextBox's `Tag` is shown as placeholder text.
  - Pill shapes bind `CornerRadius` through `PillRadiusConverter`, because a large fixed radius renders as an ellipse in WPF.
- **Pages.** Each page is mapped to a view by `DataTemplate`s in `MainWindow.xaml`. The results grid builds its columns in code (`ResultsView.xaml.cs`) and binds to `ResultRowView.Cells[i]`. Sorting uses a custom comparer (`ResultsViewModel.Sort`).
- **Typed command parameters.** A `CommandParameter` set in XAML arrives as a string, so a `RelayCommand<bool?>` throws. Use separate commands or a `string` parameter.
- **Rendering.** Views reference styles and converters defined in `App.xaml` with `StaticResource`, so rendering a view outside the running app requires `new App().InitializeComponent()` first.

### Web (`DaxDiff.Web`, Blazor WebAssembly)

- **Browser-only.** A static site: sign-in through MSAL.js (`wwwroot/lib/msal-browser`, vendored 4.x, via `wwwroot/js/auth.js`), queries through `ServiceRestConnector`, comparison and Excel export in WebAssembly. Nothing runs on a server. See `docs/web.md`.
- **State.** `ProjectState` (singleton) holds the open project, two `SourceState`s (each with its own `BrowserAuthService`, so FR-002 holds), the run and the latest result per comparison. Components derive from `ProjectComponent` to re-render on its `Changed` event.
- **Only REST.** Desktop-session sources are reported as unsupported. XMLA references are connected over REST by resolving workspace and model names. Comparison editing is not in the web app yet.
- **Files.** `wwwroot/js/files.js` uses the File System Access API where available (Save writes back), else upload and download.
- **Sample.** `SampleConnector` serves an in-memory sample project, used to try the app and for headless smoke tests.
- **Publishing.** `WasmFingerprintAssets` is off so `index.html` needs no inline import map under its CSP meta tag. Domain and Application are trimmer roots, for reflection-based project JSON. `.github/workflows/web-pages.yml` deploys to a separate public repo for GitHub Pages.

## Testing

xUnit tests are in `tests/DaxDiff.Tests.Unit`. `Fixtures/Fixture.cs` builds synthetic metadata, configs and `QueryResult`s. Its helpers are `Key(...)`, `Value(...)`, `Field(...)`, and `config.With(o => o with { ... })` for changing options. `Fixtures/FakeConnector.cs` is an in-memory `IModelConnector`; use it for orchestration tests instead of real models. Test names are prefixed with the requirement or acceptance-test ID they cover, such as `AT07_…` or `FR018_…`. The Desktop project (a WinExe) has no tests; to exercise its view models, use an offscreen harness.

Nothing has been verified against live Power BI models yet; this is Stage 0 in `docs/implementation-plan.md`. That covers Desktop attach, XMLA, REST and the DMV/INFO.VIEW queries, so treat connector behaviour against real services as unconfirmed.
