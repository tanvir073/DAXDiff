// Browser sign-in for one source (FR-002), on MSAL.js (the global `msal` from lib/msal-browser).
// One PublicClientApplication per client ID and tenant; all share the same sessionStorage cache, which the browser
// clears when the tab closes. Each source only ever uses its own account: the one it signed in with (home account ID),
// else the remembered user name. Tokens never leave the browser.

const apps = new Map();

function getApp(o) {
    const key = `${o.clientId}|${o.tenant}`.toLowerCase();
    let app = apps.get(key);
    if (!app) {
        const pca = new msal.PublicClientApplication({
            auth: {
                clientId: o.clientId,
                authority: `https://login.microsoftonline.com/${encodeURIComponent(o.tenant)}`,
                redirectUri: o.redirectUri,
                navigateToLoginRequestUrl: false,
            },
            cache: { cacheLocation: 'sessionStorage' },
        });
        app = pca.initialize().then(() => pca);
        apps.set(key, app);
    }
    return app;
}

function findAccount(app, homeAccountId, loginHint) {
    const accounts = app.getAllAccounts();
    if (homeAccountId)
        return accounts.find(a => a.homeAccountId === homeAccountId) ?? null;
    if (loginHint)
        return accounts.find(a => (a.username ?? '').toLowerCase() === loginHint.toLowerCase()) ?? null;
    return null;
}

function toToken(r) {
    return {
        accessToken: r.accessToken,
        expiresOn: r.expiresOn ? r.expiresOn.toISOString() : null,
        account: r.account?.username ?? 'unknown',
        tenantId: r.tenantId || r.account?.tenantId || '',
        homeAccountId: r.account?.homeAccountId ?? null,
    };
}

function popup(app, o, prompt) {
    return app.acquireTokenPopup({ scopes: o.scopes, prompt, loginHint: o.loginHint || undefined });
}

/** Interactive sign-in with an account picker, so each source can use a different account. */
export async function signIn(o) {
    const app = await getApp(o);
    return toToken(await popup(app, o, 'select_account'));
}

/** A token for this source's account: silently from the cache when possible, else through a pop-up. */
export async function getToken(o) {
    const app = await getApp(o);
    const account = findAccount(app, o.homeAccountId, o.loginHint);
    if (account) {
        try {
            return toToken(await app.acquireTokenSilent({ scopes: o.scopes, account }));
        } catch (e) {
            if (!(e instanceof msal.InteractionRequiredAuthError))
                throw e;
        }
    }
    return toToken(await popup(app, o, undefined));
}

/** Forgets this source's account in the browser; other sources keep theirs. */
export async function signOut(o) {
    const app = await getApp(o);
    const account = findAccount(app, o.homeAccountId, o.loginHint);
    if (account)
        await app.clearCache({ account });
}

/** Removes every account from the browser's token cache. */
export async function signOutAll(o) {
    const app = await getApp(o);
    for (const account of app.getAllAccounts())
        await app.clearCache({ account });
}
