# DAXDiff – Implementation Plan

Source of truth: `DAXDiff_Requirements.docx` (SRS v1.2, 26 Oct 2026; see its revision history). Requirement IDs (FR-xxx, NFR-xx, AT-xx) refer to that document.

## Technology decisions

| Concern | Choice | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), Windows x64 | Pinned LTS. Desktop attach and ADOMD are Windows-native |
| UI | Avalonia + MVVM (CommunityToolkit.Mvvm) | One desktop app for Windows, macOS and Linux (WPF until Nov 2026) |
| Query / metadata | Microsoft.AnalysisServices.AdomdClient 19.x (DAX + DMVs) | Same code path for Desktop and Service XMLA; DMVs avoid a TOM dependency |
| REST fallback | `HttpClient` against Power BI `executeQueries` | Bounded fallback when XMLA is unavailable (FR-003) |
| Auth | MSAL.NET + Msal.Extensions (DPAPI-encrypted token cache) | Interactive delegated sign-in, no passwords (§9) |
| Excel | ClosedXML | XLSX without needing Office |
| Logging | Serilog file sink, no measure values or DAX literals | NFR-07, §9 |
| Tests | xUnit | NFR-06 |

## Solution layout

```
src/DaxDiff.Domain        Pure domain contracts: references, metadata, mappings, config, results, project file
src/DaxDiff.Comparison    DAX builder, key normalization, comparison engine, auto-matcher, mapping validator
src/DaxDiff.Connectors    IModelConnector + Desktop / Service XMLA / Service REST connectors, session locator, MSAL auth, discovery
src/DaxDiff.Export        Excel workbook writer
src/DaxDiff.Application   Run orchestration (parallel queries, retries, timeouts, audit), project persistence
src/DaxDiff.Presentation  View models and desktop text (no UI toolkit)
src/DaxDiff.App           Avalonia shell and views
tests/DaxDiff.Tests.Unit  Unit tests (engine, DAX builder, normalization, export, persistence, orchestration via fake connectors)
```

## Status (26 Oct 2026)

Stages 1–5, 5b (SRS v1.1: projects, comparisons, aggregation, synonyms) and 5c (SRS v1.2: per-source sign-in, filters pop-up, saving comparisons) are implemented.
153 unit tests pass, covering AT-01 and AT-03 to AT-14 plus FR-002, FR-017, FR-018 and FR-020 at compiler, engine, export and persistence level, using fake connectors.
The WPF shell builds and every page renders with sample data.
**Not yet verified against live models:** Desktop session attach, XMLA, REST and DMV/INFO.VIEW metadata need Stage 0 runs against real test models.

## Stages

### Stage 0 – Technical spike *(needs real models – manual)*
- [ ] Attach to a running Power BI Desktop model (`msmdsrv` port discovery) and run sample DAX
- [ ] Authenticate to a Service workspace over XMLA with an MSAL token
- [ ] Run the same DAX through `executeQueries` REST
- [ ] Record capability results (DMV access for non-admin users, INFO.VIEW availability)

### Stage 1 – Foundation
- [x] Solution skeleton, project references, packages
- [x] Typed domain contracts (§7.1)
- [x] DI host, Serilog logging, settings file
- [x] Unit test project

### Stage 2 – Connections (FR-001..FR-005, FR-015)
- [x] `IModelConnector` abstraction plus capabilities model (FR-003, §5.3)
- [x] Desktop session locator (msmdsrv processes, workspace port files, PBIDesktop window titles) (FR-004)
- [x] External Tools launch support (`--server` / `--database` args, `.pbitool.json`) (FR-004)
- [x] Service XMLA connector with MSAL token (FR-002, FR-003)
- [x] Service REST connector with documented limits and 429/5xx backoff (FR-003, §8)
- [x] Workspace / dataset discovery through REST (FR-002)
- [x] Metadata via TMSCHEMA DMVs, MDSCHEMA fallback for non-admins, INFO.VIEW for REST (FR-005)
- [x] Error classification with side, remedial action and diagnostic code (FR-015, AT-08)

### Stage 3 – Comparisons and query (FR-006..FR-009)
- [x] Field selection per comparison, key columns (FR-006); superseded explicit pairing in v1.1
- [x] Suggestions surfaced, never guessed when ambiguous (FR-007)
- [x] Validation: missing fields, duplicates, type compatibility (FR-006, AT-14)
- [x] Safe DAX builder: SUMMARIZECOLUMNS, escaped identifiers, typed literal filters, no raw DAX (FR-009)
- [x] Row estimate query (preview cardinality)

### Stage 4 – Engine (FR-008, FR-010, §4)
- [x] Key normalization policies (trim, case, Unicode form, blank/null) with display values preserved (§4.4)
- [x] Duplicate-key detection, fail-safe by default (AT-06)
- [x] Full outer join, Only A / Only B / Both (AT-03)
- [x] Tolerance predicate, null policy, text comparison, percentage rules (§4.3, AT-04, AT-05)
- [x] Completeness: truncation, cancellation, failure → Partial / Cancelled / Failed (NFR-05, AT-07, AT-10)
- [x] Orchestrator: parallel execution, timeouts, transient retries, query hashes and audit (FR-010, FR-012)

### Stage 5 – Results and export (FR-011..FR-014)
- [x] Results grid with status filters, search, sorting, copy (FR-011)
- [x] Summary KPIs and diagnostics header (FR-012, FR-016)
- [x] Excel workbook contract, 6 sheets (§10), sheet splitting (AT-11), text-safety for `=`,`+`,`-`,`@` (AT-12)
- [x] `.pbicomp` project save/load, schema-versioned, without tokens or results (FR-014, AT-13)

### Stage 5b – SRS v1.1: projects, comparisons, aggregation, synonyms (FR-006, FR-007, FR-014, FR-017..FR-019)
- [x] Domain: `ComparisonDefinition` / `ComparisonField`, per-comparison `ComparisonOptions`, `AggregationRules`
- [x] `ComparisonCompiler`: pairing by synonym, key/aggregation/synonym checks, combined validation
- [x] DAX: aggregated column values (`SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNTA`, `DISTINCTCOUNT`, `CONCATENATEX`), keys-only comparisons
- [x] Synonym suggestions and "add matching model B fields" (FR-007)
- [x] Project file schema v2 with v1 → v2 migration; unsaved-changes tracking (FR-014)
- [x] UI: Home with New/Open project, Comparisons page (insert field or table, key, aggregation, synonym), per-comparison Options, multi-comparison Run, result picker (FR-019)

### Stage 5c – SRS v1.2: sign-in per source, filters pop-up, saving comparisons (FR-002, FR-006, FR-017, FR-020, FR-021)
- [x] Inserted fields start with no aggregation; a compared column left at None blocks the run (FR-017)
- [x] "Add same to B / A": copy selected fields to the other model by table and column name (FR-006)
- [x] Filter pop-up: basic (value list from both models, search, (Blank), is / is not) and advanced (two conditions, And/Or) filtering; typed DAX (FR-020)
- [x] Save comparison / Discard changes; runs and the project file use saved versions; drafts offered for saving on project save/close (FR-021)
- [x] Separate Power BI sign-in per source with its own tenant and account; tenant-aware XMLA address (FR-002)

### Stage 6 – Hardening *(manual or environment-dependent)*
- [ ] Run against real fixture models for AT-01 through AT-14
- [ ] Large-model performance profiling (NFR-03, NFR-04)
- [ ] Installer (MSIX), code signing, dependency/licence scan
- [ ] Accessibility pass with a screen reader (NFR-02)

## Known MVP limitations
- SUMMARIZECOLUMNS drops rows where every compared value is BLANK. A key that is missing on one side can therefore mean "member absent" or "all measures blank". The reason text says so.
- Partitioned retrieval for result sets larger than the row cap is not implemented. Runs over the cap are labelled **Partial**, never Complete.
- Hierarchies are not enumerated. Only columns and measures are listed.
- Keys-only comparisons whose keys come from several tables return every combination of key members (a warning says so).
- Concatenate joins distinct values with ", " in ascending order; very large text results may hit transport limits.
