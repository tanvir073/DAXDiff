# Results and the Excel workbook

## Run status

| Status | Meaning |
|---|---|
| **Complete** | Both models returned all rows and every row was compared |
| **Partial** | Some rows may be missing: a row limit or a REST limit was reached, or duplicate keys were reported with the *Diagnose* policy |
| **Failed** | The run stopped, for example on a connection error or on duplicate keys with the *Fail* policy |
| **Cancelled** | You cancelled the run |

A run that was cut short is never shown as Complete.

## Row status

| Status | Meaning |
|---|---|
| Match | The key exists in both models and every compared value matches |
| Mismatch | The key exists in both models and at least one value differs |
| Only in A / Only in B | The key exists in one model only |
| Invalid key | The key occurs more than once on a side, so the row is left out of the matching |

## The Results page

- The summary shows the run status and the number of matches, mismatches, keys only in A and only in B, and the match
  rate.
- **Sources, identities, settings and query hashes** shows exactly what was compared and as whom.
- Filter the grid by row status, search keys, values and reasons, and use **Columns with differences** to show only
  the compared columns that differ.
- Click a column header to sort.

The page keeps the latest result of each comparison in this session. Results are not saved in the project.

## Excel export

**Export to Excel** writes an audit workbook with these sheets:

| Sheet | Contents |
|---|---|
| Summary | Run status, both sources and the identity each was queried as, last refresh, row and key counts, match rate, totals per compared value, warnings |
| Differences | Every row that is not a match, with A, B, the difference and the % difference per value |
| All Data | Every row, if *All data* is ticked |
| Field Mapping | Which model A field was compared with which model B field, and how it was aggregated |
| Settings | Filters, tolerances, normalization and limits |
| Execution Log | Per side: transport, query hash, duration, rows, attempts, truncation and errors; the DAXDiff version |

Tick **DAX text** to include the queries themselves. Large results are split across several sheets. Values are written
as numbers, dates and text, not as formulas.
