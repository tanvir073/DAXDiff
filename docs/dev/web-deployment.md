# Web app: build and deployment

`src/DaxDiff.Web` is a Blazor WebAssembly app that runs entirely in the browser. Sign-in (MSAL.js), Power BI queries,
the comparison and the Excel workbook all run on the user's machine. The site is static files only. It shares
`Domain`, `Comparison`, `Export`, `Application` and `Connectors.Core` with the desktop app, so a comparison gives the
same result in both. What users can do with it is described in the [user guide](../user/web-app.md).

## Run locally

```powershell
dotnet run --project src/DaxDiff.Web --launch-profile http   # http://localhost:5215
```

**Open sample** on the Project page runs a comparison over two in-memory models (`SampleConnector`), without sign-in.
For local sign-in, add `http://localhost:5215/redirect.html` to a single-page-application registration (see the
[administrator guide](../user/administrators.md#app-registration-for-the-web-app)).

## What the browser can reach

| Source | Web | Why |
|---|---|---|
| Service over the Execute Queries REST API | Yes | The only Power BI transport a browser can call |
| Service over XMLA | No | ADOMD.NET is a native client. XMLA references are connected over REST by resolving workspace and model names. |
| Power BI Desktop | No | A browser cannot reach the local Analysis Services port or run WMI discovery |

Not in the web app yet: creating and editing comparisons, options and filters, and splitting large results across
several REST queries.

## GitHub Pages

`.github/workflows/web-pages.yml` runs the unit tests, publishes the web app and deploys it to
`https://tanvir073.github.io/DAXDiff/` on pushes to `main` and `dev` that touch the web app or the shared projects.
On the GitHub Free plan, Pages only serves public repositories.

One-time setup:

1. *Settings → Pages → Build and deployment*: source **GitHub Actions**.
2. *Settings → Environments → github-pages*: allow `main` and `dev`.
3. Optional *Settings → Secrets and variables → Actions → Variables*:
   - `DAXDIFF_CLIENT_ID`: the Entra client ID baked into `appsettings.json` (not a secret);
   - `PAGES_BASE_PATH`: defaults to `/<repository name>/`; use `/` for a custom domain.

The workflow sets `<base href>`, copies `index.html` to `404.html` so deep links work, adds `.nojekyll` so Pages
serves `_framework/`, and removes the precompressed `.br`/`.gz` files that Pages ignores.

Each release also attaches `DAXDiff-<version>-web.zip` for organisations that host their own copy.

## Security and size

- Tokens are kept in the tab's session storage. The content security policy in `index.html` only allows network
  access to `login.microsoftonline.com` and `api.powerbi.com`. Static hosts cannot send headers, so the policy is a
  meta tag.
- `WasmFingerprintAssets` is off, so `index.html` needs no inline import map and the policy can forbid inline scripts.
- Domain and Application are trimmer roots, because project JSON uses reflection.
- The site is public. Do not put anything confidential in `wwwroot/appsettings.json`.
- The first visit downloads about 5 MB (compressed), which the browser caches.
