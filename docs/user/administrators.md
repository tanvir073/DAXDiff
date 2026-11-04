# Administrator guide

For the person who sets DAXDiff up for an organisation. Users can compare Power BI Desktop files and connect over
XMLA with no setup. An Entra ID app registration is needed for REST queries and workspace browsing, and the web app
always needs one.

DAXDiff only needs **delegated** read permissions. Every query runs as the signed-in user, with that user's access.
DAXDiff has no server of its own and sends no telemetry.

## Power BI tenant and capacity settings

| Setting | Needed for |
|---|---|
| Capacity: **XMLA endpoint = Read** (or Read Write) on Premium, PPU or Fabric capacity | XMLA connections (desktop app) |
| Tenant: **Semantic model Execute Queries REST API** (Admin portal → Integration settings) | REST connections (desktop and web) |
| **Build** permission on each semantic model, for each user | Every Service connection |

## App registration for the desktop app

1. Entra admin center → **App registrations** → *New registration*. Name it `DAXDiff` and choose accounts in this
   organisational directory.
2. **Authentication** → *Add a platform* → *Mobile and desktop applications* → redirect URI `http://localhost`.
   Turn on *Allow public client flows*.
3. **API permissions** → *Power BI Service* → delegated `Dataset.Read.All` and `Workspace.Read.All`. Grant admin
   consent if your tenant requires it.
4. Give users the *Application (client) ID*. They enter it under **Settings** in DAXDiff and restart.

A client ID is not a secret. To set it for every user of a deployed copy, put it in `appsettings.json` next to
`DAXDiff.exe`:

```json
{ "AzureAd": { "ClientId": "00000000-0000-0000-0000-000000000000", "TenantId": "organizations" } }
```

A user's own setting in `%LOCALAPPDATA%\DAXDiff\settings.json` overrides it.

### Models in other tenants

A user can sign in to a model in another organisation by entering that tenant's domain or ID in the source's
**Tenant** field. If the registration should work for guests from other tenants, choose *Accounts in any organisational
directory* when you register it.

## App registration for the web app

1. *New registration*: name it `DAXDiff web`. Choose accounts in this organisational directory, or in any organisational
   directory if users from other tenants will sign in.
2. **Authentication** → *Add a platform* → **Single-page application**, with the redirect URI of each site that hosts
   the app, ending in `/redirect.html`. For the public site that is `https://tanvir073.github.io/DAXDiff/redirect.html`.
   The web app's Settings page shows the exact URI for the site you are on.
3. **API permissions**: as for the desktop app.
4. Give users the client ID. They enter it on the web app's **Settings** page; it is kept in that browser only.

## Deploying the desktop app

`DAXDiff-<version>-win-x64.msi` installs per machine, so it can be deployed with Intune, Configuration Manager or
Group Policy. A silent install is:

```powershell
msiexec /i DAXDiff-0.1.0-win-x64.msi /qn
```

The MSI also registers DAXDiff as a Power BI Desktop External Tool. Newer versions replace older ones. DAXDiff is not
yet code-signed. If application control (WDAC, AppLocker) blocks unsigned programs, allow the install folder or the
file hash.

## Hosting the web app

Each release includes `DAXDiff-<version>-web.zip`, a static site that can be served by any web server (IIS, Azure
Static Web Apps, an internal file server with HTTPS). Unzip it, then:

- If it is not served at the root of the host, change `<base href="/" />` in `index.html` to the folder, such as
  `/daxdiff/`.
- To supply the client ID, set `Auth.ClientId` in `appsettings.json`.
- Serve `index.html` for unknown paths too (or copy it to `404.html`), so deep links work.
- Add the site's `redirect.html` URI to the app registration.

The site only serves files. All sign-ins and queries go from the user's browser straight to Microsoft.
