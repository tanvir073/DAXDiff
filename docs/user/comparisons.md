# Building comparisons

A project can hold any number of comparisons. Each one has its own fields, filters and options, and is run on its own.

## Fields

On the **Comparisons** page, insert fields from each model's tree:

- select columns or measures and click **Insert into A** / **Insert into B** (or double-click, or press Enter);
- select a table to insert all its columns;
- Ctrl+click and Shift+click select several fields;
- **Show hidden fields** includes hidden tables and fields.

**Add same to B ▸** and **◂ Add same to A** copy the selected rows to the other model by the same table and column
name (measures by name), with the same key, aggregation and synonym. **Find equivalent B fields** looks for model B
fields with an equivalent name for model A fields that have no counterpart yet.

The order of the key fields is the order of the key columns in the results. Use the arrows or drag rows to change it.

## Keys

Tick **Key** for the columns that identify a row, such as `Customer ID`, or `Region` and `Year`. Keys define the grain:
DAXDiff evaluates both models at this grain and pairs rows whose keys are equal.

- Every key must be marked on both sides, with the same synonym.
- Keys are compared as values of their type. Text is never turned into a number, so `0012` and `12` are different keys.
  Numbers compare by value, so `1.20` equals `1.2`.
- A comparison with only keys and no values is allowed. It shows which keys exist on one side only.

## Aggregations

A non-key column is aggregated to the key grain: **Sum**, **Average**, **Min**, **Max**, **Count**, **Distinct count**
or **Concatenate** (the distinct values joined into one text). Which ones are offered depends on the column's type.
New columns start with Sum for numbers, Concatenate for text and Max for true/false ("any TRUE"). Dates and columns
of unknown type start at **None**, and you must choose an aggregation before the comparison can be saved.

Measures are evaluated as they are defined in the model, so they have no aggregation.

## Synonyms

Fields are compared when they have the **same synonym** on both sides. Synonyms are not case-sensitive, and leading or
trailing spaces are ignored. This lets differently named fields be compared, such as `Sales[Amount]` with
`Fact[NetAmount]`. **Match synonyms** gives model B fields the synonym of the equivalent model A field.

The *Compared pairs* tab shows what will be compared. *Problems and warnings* lists anything that stops the
comparison from running, such as a key without a counterpart, two fields with the same synonym on one side, or types
that cannot be compared.

## Saving

Changes are a draft until you click **Save comparison**. Runs and the project file always use the saved version.
*Discard changes* returns to the last saved version. When you save or close the project, DAXDiff offers to save
unsaved comparisons.

## Filters

**Filters** restrict both models to the same slice of data, as in Power BI's filter pane. Each filter names a model A
column and the matching model B column, and the same rule applies to both.

- **Basic filtering:** keep (*Is*) or remove (*Is not*) the selected values. The value list is loaded from both
  models and can be searched; you can also type a value.
- **Advanced filtering:** one or two conditions (is, is not, less than, greater than, contains, starts with, and
  their opposites), joined by *And* or *Or*.

Write numbers with `.` as the decimal separator and dates as `yyyy-mm-dd` (optionally `hh:mm:ss`). Text matching is not
case-sensitive. *Is* never treats BLANK as 0. A filter's description is included in the Excel export.

## Options

The **Options** page shows the options of the comparison you pick. Nothing is rounded, trimmed or treated as equal
unless you choose it here.

### Value comparison

| Option | Meaning |
|---|---|
| Mode | *Exact*, or *Tolerance*: values match when \|A − B\| ≤ max(absolute, relative × max(\|A\|, \|B\|)) |
| Absolute / relative tolerance | For example `0.01`, and `0.001` for 0.1% |
| Null policy | *Strict* (BLANK only equals BLANK) or *Blank equals zero* |
| Text value comparison | *Ordinal (case-sensitive)* or *Ordinal, ignore case* |
| Percentage difference baseline | Whether the % difference is relative to model A or model B |

### Key normalization

Trim whitespace, case-insensitive keys, treat BLANK and empty text as the same key, compare date keys on the date only,
and Unicode normalization (NFC or NFKC). Use these when the two models store the same key slightly differently.

**Duplicate key policy:** if two rows end up with the same key (for example after normalization), *Fail the run*
stops with an error, and *Diagnose* reports those rows as *Invalid key* and leaves them out of the matching. Duplicates
are never merged silently.

### Limits

Maximum rows per side, timeout and the number of retries on temporary errors. A side that reaches the row limit makes
the run **Partial**.

### Result rows

Show matches and differences, or differences only, and optionally limit the number of result rows. The summary always
counts every row; a limit only shortens the list, keeping differences before matches.

### Query preview

**Preview DAX** shows the query sent to each model, and **Estimate rows** counts the rows each side will return.
