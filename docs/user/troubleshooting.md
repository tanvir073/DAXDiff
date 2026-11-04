# Troubleshooting and reporting bugs

Every error DAXDiff shows has a **diagnostic code** (such as `REST-403`) and a suggested remedy. Include the code when
you ask for help.

## Common problems

| Problem | What to check |
|---|---|
| No Power BI Desktop sessions are found | The PBIX must be open in Power BI Desktop and fully loaded. Click *Detect sessions* again after it has loaded. |
| "Access is denied" when DAXDiff starts | Your organisation blocks unsigned programs. See [Install DAXDiff](installation.md#windows-protected-your-pc-and-blocked-programs). |
| Sign-in or workspace browsing says a client ID is missing | Ask your administrator for the client ID and enter it under **Settings**. XMLA works without one if you type the names. |
| XMLA connection fails | The workspace must be on Premium, PPU or Fabric capacity with the XMLA endpoint enabled, and you need Build permission. |
| REST returns 401 or 403 | The tenant setting *Semantic model Execute Queries REST API* must be on, and you need Build permission on the model. |
| The run is **Partial** | A row limit or a REST limit was reached, or duplicate keys were reported. The Results page and the workbook say which. Add filters or raise *Max rows per side*. |
| Rows are "only in A" although the data looks the same | Check key types and formatting: `0012` and `12` are different keys, and so are `ABC` and `abc` unless you turn on case-insensitive keys. Row-level security can also hide rows on one side. |
| Values differ by tiny amounts | Choose *Tolerance* mode on the Options page. |
| The comparison will not save or run | Open the *Problems and warnings* tab at the bottom of the Comparisons page. |

## Files and locations (desktop app)

| What | Where |
|---|---|
| Logs (no values or DAX text) | `%LOCALAPPDATA%\DAXDiff\logs`, or Settings → **Open log folder** |
| Your settings | `%LOCALAPPDATA%\DAXDiff\settings.json` |
| Sign-in cache (encrypted, per user) | `%LOCALAPPDATA%\DAXDiff\msal.cache` |
| Projects | Wherever you save the `.pbicomp` files |

Settings → **Reset session** closes the current project (after asking) and returns to the start page.

## Reporting a bug

Settings → **Help and feedback** → **Report a bug…** opens a GitHub issue form in your browser, with your DAXDiff
version and operating system filled in. You need a free GitHub account. You can also open the form directly at
<https://github.com/tanvir073/DAXDiff/issues/new/choose>.

A good report includes the diagnostic code, the steps that lead to the problem and what you expected. Do not paste
tokens, passwords or confidential data from your models; replace real values with made-up examples. A short log
excerpt often helps. Logs contain no values or DAX text, but check them for names you would rather not share.

Suggestions are welcome too: choose **Feature request** on the same page.

**Security problems** should be reported privately, not as an issue: see [SECURITY.md](../../SECURITY.md).
