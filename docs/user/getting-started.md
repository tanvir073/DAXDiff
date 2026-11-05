# Getting started

This walkthrough compares one value, such as sales amount by region and year, between two models. It uses the desktop
app; the web app follows the same steps for projects that already contain comparisons.

## Before you start

- **Power BI Desktop file:** open the PBIX in Power BI Desktop and wait until it has loaded.
- **Power BI Service model:** you need Build permission on it. XMLA also needs Premium, Premium Per User or Fabric
  capacity. REST, and browsing workspaces, need a client ID from your administrator (see
  [Connecting to models](connecting-sources.md)).

## 1. Create a project

On the start page choose **Start new project** and pick where to save the `.pbicomp` file. A project holds the two
models and all your comparisons. It never holds results, tokens or passwords, so you can share it with colleagues.

## 2. Connect model A and model B (Sources)

For each side, choose the kind of source:

- **Power BI Desktop:** click *Detect sessions* and choose the open file.
- **Power BI service (XMLA)** or **(REST):** sign in, then type or browse to the workspace and the semantic model.

Click **Test connection**. DAXDiff loads the model's tables, columns and measures. The two sides can be any mix: PBIX
and PBIX, PBIX and Service, or two Service models, even in different tenants.

## 3. Build a comparison (Comparisons)

1. Click **+ New comparison** and give it a name.
2. In the model A tree, select `Region` and `Year`, then **Insert into A**. Insert `Amount` (or a measure) too.
3. With the rows selected, click **Add same to B ▸** to add the same fields to model B, or insert them from model B's
   tree yourself.
4. Tick **Key** for `Region` and `Year` on both sides. Keys define the grain: one result row per region and year.
5. Make sure the fields that belong together have the same **Synonym** on both sides. *Match synonyms* does this for
   you when the names are similar.
6. Check *Compared pairs* and *Problems and warnings* at the bottom, then click **Save comparison**.

Runs always use the saved version. A comparison marked `*` has unsaved changes.

## 4. Choose the rules (Options)

The defaults compare exactly: nothing is rounded, trimmed or treated as equal unless you choose it. You might want a
tolerance for amounts (for example an absolute tolerance of `0.01`) or case-insensitive keys. Save the comparison
again after changing options.

## 5. Run

Tick the comparisons to run, confirm the note on security ("as observed by the current caller"), then click **Run**
(F5). Both models are queried at the same time.

## 6. Review and export (Results)

The summary shows matches, mismatches and keys found only in A or only in B. Filter the grid by status, search it, or
show only columns with differences. **Export to Excel** writes the audit workbook.

Next: [Building comparisons](comparisons.md) explains every setting in detail.
