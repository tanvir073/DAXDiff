# Connecting to models

Each side of a project (model A and model B) is one source. The sides are independent, so they can be different
kinds of source, in different tenants, and signed in as different users.

**Every query runs as you.** Row-level security, object-level security and roles apply to your account. A Power BI
Desktop session runs with administrator rights, so no row-level security applies there. Rows can therefore differ
because of security rather than because the models differ. Results are labelled "as observed by the current caller"
for this reason.

## Power BI Desktop (local PBIX)

Desktop app only, on Windows.

1. Open the PBIX in Power BI Desktop and wait until the model has loaded.
2. On **Sources**, choose *Power BI Desktop*. Optionally browse to the PBIX so DAXDiff can pick the matching session.
3. Click **Detect sessions**. Each open file is listed with its name or window title and its local port.
4. Choose the session and click **Test connection**.

DAXDiff never opens PBIX files itself; the file must be open in Power BI Desktop. A session's port changes each time
Power BI Desktop starts, so after reopening a project you detect the session again.

### From Power BI Desktop's ribbon

If DAXDiff was installed with the MSI, **External Tools → DAXDiff** in Power BI Desktop starts DAXDiff with the current
file as model A. Create or open a project and the file is connected.

## Power BI Service over XMLA (preferred for Service models)

Desktop app only. Needs:

- a workspace on Premium, Premium Per User or Fabric capacity, with the XMLA endpoint set to *Read* or *Read Write*;
- Build permission on the semantic model.

1. Choose *Power BI service (XMLA)*.
2. Set **Tenant**: leave `organizations` for your own tenant. For a model in another organisation, enter that tenant's
   domain (`contoso.com`) or tenant ID.
3. Click **Sign in…** and choose the account.
4. Type the workspace and semantic-model names, or browse to them (browsing needs a client ID; see below).
5. Click **Test connection**.

Without a client ID, XMLA still works: type the names and test, and Microsoft's sign-in window appears.

## Power BI Service over REST

Desktop and web app. Uses Power BI's *Execute Queries* API. Needs:

- a client ID from your administrator, entered under **Settings**;
- the tenant setting *Semantic model Execute Queries REST API* to be enabled;
- Build permission on the semantic model.

REST has limits: 100,000 rows or 1,000,000 values, and 15 MB per query. A result that reaches a limit is reported as
**Partial**, never as Complete, so you always know when rows were cut off. Filter the comparison or use XMLA for
larger results.

## Signing out

Settings → **Sign out of all accounts** removes every cached sign-in. Tokens are never saved in projects, logs or
workbooks. The project only remembers the tenant and the last account name, to suggest them next time.
