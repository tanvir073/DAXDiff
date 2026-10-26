# DAXDiff

Windows desktop app that compares evaluated business data from two Power BI semantic models. It lists every key that matches, differs, or exists on one side only, and exports the results to an Excel audit workbook.

Work is organised in **projects** (`.pbicomp` files). A project holds the two models and any number of **comparisons**. In each comparison you:

- insert any columns (or a whole table's columns) and measures from each model;
- mark the key columns;
- choose an aggregation for the other columns (Sum, Average, Min, Max, Count, Distinct count, Concatenate);
- give fields that should be compared the same **synonym** on both sides.

Any pairing works: local PBIX (open in Power BI Desktop) ↔ PBIX, PBIX ↔ Service, or Service ↔ Service. Service models are reached over XMLA, with the REST API as a bounded fallback.

- Requirements: `DAXDiff_Requirements.docx` (SRS v1.1)
- Implementation plan and status: [docs/implementation-plan.md](docs/implementation-plan.md)
- Setup, sign-in and External Tools registration: [docs/setup.md](docs/setup.md)

```powershell
dotnet build
dotnet test
dotnet run --project src/DaxDiff.Desktop
```

## Guarantees

- **Read-only.** Models are only queried, never modified or refreshed.
- **No silent truncation.** A row cap, a REST limit, a cancellation or a duplicate key makes the run *Partial*, *Cancelled* or *Failed*, never *Complete*.
- **Security as observed.** Each side is queried as the current caller. Results are labelled that way, and RLS caveats must be acknowledged before a run.
- **No secrets at rest.** Projects (`.pbicomp`), logs and workbooks never contain tokens or passwords. Logs contain no measure values or DAX literals.

## Structure

| Project | Responsibility |
|---|---|
| `DaxDiff.Domain` | Contracts: model references, metadata, comparison definitions and aggregation rules, config, results, project file |
| `DaxDiff.Comparison` | Synonym compiler, DAX builder, key normalization, comparison engine, auto-matcher, validator |
| `DaxDiff.Connectors` | Desktop session, Service XMLA and Service REST connectors, session discovery, MSAL |
| `DaxDiff.Export` | Excel workbook (Summary, Differences, All Data, Field Mapping, Settings, Execution Log) |
| `DaxDiff.Application` | Run orchestration (parallel, retries, timeouts, audit), project persistence |
| `DaxDiff.Desktop` | WPF MVVM shell |
| `DaxDiff.Tests.Unit` | xUnit tests mapped to SRS acceptance tests |
