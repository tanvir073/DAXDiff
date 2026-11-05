# DAXDiff user guide

DAXDiff compares the data two Power BI semantic models return. You choose the fields to compare and the key columns
that identify a row. DAXDiff then queries both models at that grain and lists every row that matches, differs or
exists on one side only. The whole result can be exported to an Excel audit workbook.

DAXDiff only reads models. It never changes or refreshes them.

## Contents

1. [Install DAXDiff](installation.md): desktop app for Windows, macOS and Linux, or the web app in any browser
2. [Getting started](getting-started.md): your first comparison, step by step
3. [Connecting to models](connecting-sources.md): Power BI Desktop, Service over XMLA, Service over REST
4. [Building comparisons](comparisons.md): fields, keys, aggregations, synonyms, filters and options
5. [Results and the Excel workbook](results.md)
6. [The web app](web-app.md): what works in the browser, and what does not
7. [Troubleshooting and reporting bugs](troubleshooting.md)

For the person who sets DAXDiff up for an organisation: [Administrator guide](administrators.md) (Entra ID app
registration, tenant settings, deployment).

## Which app should I use?

| | Desktop app | Web app |
|---|---|---|
| Runs on | Windows 10 and 11, macOS 12 or later, Linux (x64 and ARM) | Any modern browser |
| Install | Windows: MSI or portable zip. macOS: .dmg. Linux: .tar.gz | Nothing to install |
| Power BI Desktop files (PBIX) | Windows only | No |
| Power BI Service over XMLA | Yes | Connected over REST instead |
| Power BI Service over REST | Yes | Yes |
| Create and edit comparisons | Yes | Not yet: open a project made in the desktop app |
| Excel workbook | Yes | Yes |
