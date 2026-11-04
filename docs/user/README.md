# DAXDiff user guide

DAXDiff compares the data two Power BI semantic models return. You choose the fields to compare and the key columns
that identify a row. DAXDiff then queries both models at that grain and lists every row that matches, differs or
exists on one side only. The whole result can be exported to an Excel audit workbook.

DAXDiff only reads models. It never changes or refreshes them.

## Contents

1. [Install DAXDiff](installation.md): Windows desktop app, or the web app in any browser
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
| Runs on | Windows 10 and 11 (x64) | Any modern browser on Windows, macOS or Linux |
| Install | MSI installer or portable zip | Nothing to install |
| Power BI Desktop files (PBIX) | Yes | No |
| Power BI Service over XMLA | Yes | Connected over REST instead |
| Power BI Service over REST | Yes | Yes |
| Create and edit comparisons | Yes | Not yet: open a project made in the desktop app |
| Excel workbook | Yes | Yes |

A native desktop app for macOS and Linux is planned. Until it ships, use the web app on those systems.
