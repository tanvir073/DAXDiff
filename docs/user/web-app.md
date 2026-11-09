# The web app

**https://tanvir073.github.io/DAXDiff/** runs DAXDiff in your browser on Windows, macOS or Linux. Nothing is
installed.

Sign-in, Power BI queries, the comparison and the Excel workbook all happen on your computer. The site only delivers the
app's files; it never receives your credentials, queries or data.

## What it can do

| | Web app |
|---|---|
| Open and save `.pbicomp` projects | Yes |
| Power BI Service over REST | Yes |
| Power BI Service over XMLA | Connected over REST instead: the workspace and model are found by name |
| Power BI Desktop (local PBIX) | No: a browser cannot reach Power BI Desktop |
| Create and edit comparisons, options and filters | Not yet: build them in the desktop app, save the project and open it here |
| Run, review results, export to Excel | Yes |

REST limits each query to 100,000 rows or 1,000,000 values. A result that reaches a limit is reported as Partial.

## First use

1. Open the site and go to **Settings**. If the site does not supply a client ID, enter the one your administrator gave
   you (see the [administrator guide](administrators.md)).
2. On **Project**, open a `.pbicomp` file. **Open sample** runs a comparison over two built-in demo models without
   signing in.
3. On **Sources**, sign in for model A and model B.
4. **Run**, then review and export on **Results**.

In Edge and Chrome, **Save** writes back to the file you opened. Other browsers download a copy instead.

## Privacy

- Sign-in uses a Microsoft pop-up. Tokens are kept in this tab's session storage and are deleted when the tab closes.
- Queries go straight from your browser to `api.powerbi.com`.
- Results stay in the browser's memory until you export them.

The first visit downloads about 5 MB, which the browser then caches.
