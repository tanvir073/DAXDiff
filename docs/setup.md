# DAXDiff – Setup and run

## Prerequisites

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build) or .NET 10 Desktop Runtime (to run)
- Power BI Desktop (for local PBIX comparisons)
- For Power BI Service models: Build permission on each semantic model

## Build, test, run

```powershell
dotnet build
dotnet test
dotnet run --project src/DaxDiff.Desktop
# or package:
./scripts/build.ps1            # Release build + tests + publish to ./publish
```

## Installer

```powershell
./scripts/package.ps1          # tests, self-contained publish to ./publish/app, MSI to ./dist
```

`dist/DAXDiff-<version>-x64.msi` installs DAXDiff for all users (administrator rights required):
- The app goes to `C:\Program Files\DAXDiff`. It is self-contained, so the target laptop needs no .NET runtime.
- The installer adds a Start menu shortcut and registers DAXDiff on Power BI Desktop's External Tools ribbon.
- Installing a newer version replaces the old one. Uninstall from *Settings → Apps*.

The installer is built with WiX Toolset 5 (a NuGet build SDK, so nothing needs installing). Raise `<Version>` in
`Directory.Build.props` for each release, or Windows Installer treats it as the same version.
Neither the MSI nor `DAXDiff.exe` is code-signed yet (§9): Windows SmartScreen warns about the download, and
application-control policies may block the exe.

> If your organisation's application-control policy blocks unsigned executables ("Access is denied" when starting
> `DAXDiff.exe`), start it through the trusted .NET host instead: `dotnet DAXDiff.dll` from the output folder.
> Release builds should be code-signed (§9).

## Comparing local PBIX files

1. Open the PBIX in Power BI Desktop and wait until the model has loaded.
2. In DAXDiff → **Sources**, choose *Power BI Desktop*, optionally browse to the PBIX, then **Detect sessions**.
3. Pick the right session: each one shows the file name or window title and its local port. Then click **Test connection**.

DAXDiff never opens PBIX files offline. Desktop sessions run with model-administrator rights, so row-level security is not applied.

### Optional: register as a Power BI Desktop External Tool

Copy `tools/DAXDiff.pbitool.json` to
`%CommonProgramFiles(x86)%\Microsoft Shared\Power BI Desktop\External Tools\` (administrator rights required).
First edit `path` to point at your installed `DAXDiff.exe`. DAXDiff then shows on the **External Tools** ribbon and opens with
model A already attached to the current file (`--server`/`--database` arguments).

## Power BI Service

### Signing in per source

Each Service source on the Sources page signs in separately. Set the **Tenant** field:

- Leave it as `organizations` for a model in your own tenant.
- For a model in another tenant, enter that tenant's domain (`contoso.com`) or tenant ID.

Then click **Sign in…**, which lets you pick the account. Model A and model B can be in the same tenant or in different tenants, signed in as the same user or as different users. The tenant and the last account name are saved in the project as hints; tokens are not.

### XMLA (preferred)

- The workspace must be on Premium, Premium Per User or Fabric capacity, with **XMLA endpoint = Read** (or Read Write) in capacity settings.
- Enter the workspace and semantic-model names, or browse after signing in. Without a client ID, ADOMD.NET's built-in sign-in prompt is used.

### REST fallback

Uses the [Execute Queries API](https://learn.microsoft.com/en-us/rest/api/power-bi/datasets/execute-queries).
Limits: 100,000 rows or 1,000,000 values, and 15 MB per query. Results that reach a limit are always reported **Partial**.
Requires the tenant setting *Dataset Execute Queries REST API* and an Entra app registration (below).

### Entra ID app registration (needed for workspace browsing and REST)

1. Entra admin center → **App registrations** → *New registration*: name `DAXDiff`, accounts in this organisational directory.
2. **Authentication** → *Add a platform* → *Mobile and desktop applications* → redirect URI `http://localhost`. Enable *Allow public client flows*.
3. **API permissions** → *Power BI Service* → delegated `Dataset.Read.All` and `Workspace.Read.All`. Grant admin consent if your tenant requires it (request a human review first, per §13.3).
4. Copy the *Application (client) ID* into DAXDiff → **Settings** (or `AzureAd:ClientId` in `appsettings.json`), set the tenant, and restart.

Tokens are kept in a per-user, DPAPI-encrypted MSAL cache (`%LOCALAPPDATA%\DAXDiff\msal.cache`). They are never written to projects, logs or exports.

## Files and locations

| What | Where |
|---|---|
| Logs (no values or DAX text) | `%LOCALAPPDATA%\DAXDiff\logs` |
| User settings override | `%LOCALAPPDATA%\DAXDiff\settings.json` |
| Projects | `*.pbicomp` (JSON, schema-versioned, settings only) |
