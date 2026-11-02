# DAXDiff web – setup, run and deploy

DAXDiff web (`src/DaxDiff.Web`) is a Blazor WebAssembly app that runs entirely in the browser. Sign-in (MSAL.js),
Power BI queries, the comparison and the Excel workbook all happen on the user's machine. The site is static files
only; the host never receives credentials, queries or data.

It shares `Domain`, `Comparison`, `Export`, `Application` and `Connectors.Core` with the desktop app, so a comparison
gives the same result in both.

## What the browser can and cannot reach

| Source | Web | Why |
|---|---|---|
| Power BI Service model over the Execute Queries REST API | Yes | The only Power BI transport a browser can call |
| Power BI Service model over XMLA | No | ADOMD.NET is a native client. Projects that use XMLA are connected over REST, resolving the workspace and model by name. |
| Model open in Power BI Desktop | No | A browser cannot reach the local Analysis Services port or run WMI discovery |

The REST API limits each query to 100,000 rows or 1,000,000 values (FR-003, §5.2). A result that reaches a limit is
reported as Partial, never Complete (NFR-05). The REST API also needs:

- Build permission on each semantic model.
- The tenant setting *Semantic model Execute Queries REST API* (Power BI admin portal → Integration settings).

### Not in the web app yet

- Creating and editing comparisons, options and filters. Do this in the desktop app, save the project and open it in the web app.
- Splitting large results across several REST queries.

## Run locally

```powershell
dotnet run --project src/DaxDiff.Web --launch-profile http   # http://localhost:5215
```

**Open sample** on the Project page runs a comparison over two in-memory models, so you can try the app without
signing in.

## Entra ID app registration (single-page application)

1. Entra admin center → **App registrations** → *New registration*. Name it `DAXDiff web` and choose accounts in this
   organisational directory, or in any organisational directory if users from other tenants will sign in (FR-002).
2. **Authentication** → *Add a platform* → **Single-page application**. Add one redirect URI per place the site runs:
   - `http://localhost:5215/redirect.html` (local development)
   - `https://<user>.github.io/<site-repo>/redirect.html` (GitHub Pages)

   Settings in the web app shows the exact URI for the current site.
3. **API permissions** → *Power BI Service* → delegated `Dataset.Read.All` and `Workspace.Read.All`. Grant admin
   consent if your tenant requires it.
4. Give the *Application (client) ID* to users, who enter it on the web app's **Settings** page, or build it into the
   site with the `DAXDIFF_CLIENT_ID` variable (below). A client ID is not a secret.

Tokens are kept in the tab's session storage and are cleared when the tab closes. They never leave the browser except
to Microsoft Entra ID and the Power BI API. The content security policy in `index.html` only allows network access to
`login.microsoftonline.com` and `api.powerbi.com`.

## Deploy to GitHub Pages (Free plan, private source)

On the GitHub Free plan, Pages only serves public repositories. The workflow `.github/workflows/web-pages.yml` keeps
this repository private: it builds the site and pushes only the compiled files to a separate public repository.

1. Create an empty **public** repository, for example `tanvir073/daxdiff-web`.
2. Create a deploy key pair: `ssh-keygen -t ed25519 -C daxdiff-pages -f daxdiff-pages -N ""`.
   - In the **site** repository: *Settings → Deploy keys → Add*, paste `daxdiff-pages.pub` and tick **Allow write access**.
   - In **this** repository: *Settings → Secrets and variables → Actions → New repository secret* named
     `PAGES_DEPLOY_KEY`, with the contents of `daxdiff-pages` (the private key). Then delete both local files.
3. In this repository, under *Actions → Variables*, add:
   - `PAGES_REPOSITORY` = `tanvir073/daxdiff-web`
   - `DAXDIFF_CLIENT_ID` = the Entra client ID (optional)
   - `PAGES_BASE_PATH` (optional). The default is `/<site repo name>/`; use `/` for a custom domain.
4. Run the workflow (*Actions → Web (GitHub Pages) → Run workflow*). After that it runs on every push to `main` that
   touches the web app or the shared projects.
5. In the **site** repository: *Settings → Pages → Build and deployment*, source *Deploy from a branch*, branch
   `gh-pages`, folder `/ (root)`.

The site appears at `https://tanvir073.github.io/daxdiff-web/`. Add its `redirect.html` URI to the app registration.

The workflow sets the `<base href>`, copies `index.html` to `404.html` so deep links work, adds `.nojekyll` so Pages
serves `_framework/`, and removes the precompressed `.br`/`.gz` files that Pages ignores.

The site itself is public. Anyone can load it, but every query needs the visitor's own Microsoft sign-in and
permissions on the model. Do not put anything confidential in `wwwroot/appsettings.json`.

## Size and caching

The first visit downloads about 5 MB (compressed): the .NET runtime, the shared DAXDiff assemblies and the Excel
libraries. The browser caches it after that. Fingerprinted file names are turned off (`WasmFingerprintAssets`), so
`index.html` needs no inline import map and the content security policy can forbid inline scripts.
