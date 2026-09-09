# External login (OAuth2) setup

Google, Microsoft, and GitHub sign-in are **wired but config-gated** (parity plan
P3.4). Each provider registers in `Program.cs` **only when its `ClientId` is
present in configuration**. With nothing configured — the default for a fresh
clone — `SignInManager.GetExternalAuthenticationSchemesAsync()` returns an empty
list, so `Login.razor`'s `_externalLogins` is empty and the **"Or sign in with"
section does not render**. That is expected, not a bug: no buttons means no
provider is configured.

To get the buttons, register a real OAuth app with at least one provider and put
its credentials in configuration.

## Configuration keys

| Key | Notes |
|---|---|
| `Authentication:Google:ClientId` / `:ClientSecret` | first-party `Microsoft.AspNetCore.Authentication.Google` |
| `Authentication:Microsoft:ClientId` / `:ClientSecret` | first-party `.MicrosoftAccount` |
| `Authentication:GitHub:ClientId` / `:ClientSecret` | community `AspNet.Security.OAuth.GitHub` (Microsoft ships none) |

- **Dev:** user-secrets only — never `appsettings*.json`.
- **Prod:** environment variables, e.g. `Authentication__Google__ClientId`.
- Only the providers you configure appear; the others stay hidden. Configuring
  one is enough.
- Restart the app after changing secrets — configuration is read at startup.

## Callback / redirect URIs

Callback paths are the handler defaults and are **not** configurable here:

| Provider | Callback path |
|---|---|
| Google | `/signin-google` |
| Microsoft | `/signin-microsoft` |
| GitHub | `/signin-github` |

The full redirect URI you register with the provider is the app origin plus that
path. For local dev with the `https` launch profile
(`Properties/launchSettings.json`, `applicationUrl` `https://localhost:7105`):

```
https://localhost:7105/signin-google
https://localhost:7105/signin-microsoft
https://localhost:7105/signin-github
```

Use HTTPS — Google and Microsoft reject plain-HTTP redirect URIs except for the
`localhost` exemption Google still allows. Register the production origin's
equivalents (`https://<host>/signin-<provider>`) separately when deploying.

## Per-provider registration

### GitHub (quickest)

1. GitHub → **Settings** → **Developer settings** → **OAuth Apps** → **New OAuth App**.
2. Homepage URL: `https://localhost:7105`.
   Authorization callback URL: `https://localhost:7105/signin-github`.
3. Register, then **Generate a new client secret**.
4. Set the secrets:

   ```bash
   dotnet user-secrets set "Authentication:GitHub:ClientId" "<client-id>"
   dotnet user-secrets set "Authentication:GitHub:ClientSecret" "<client-secret>"
   ```

`ExternalLogin.razor` reads the primary verified email; the handler requests the
`user:email` scope so private emails still come through.

### Google

1. [Google Cloud Console](https://console.cloud.google.com/) → create or pick a
   project.
2. **APIs & Services** → **OAuth consent screen** → configure (External, add your
   account as a test user while it's unpublished).
3. **APIs & Services** → **Credentials** → **Create Credentials** → **OAuth client ID**
   → **Web application**.
4. Authorized redirect URIs: `https://localhost:7105/signin-google`.
5. Set the secrets:

   ```bash
   dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>.apps.googleusercontent.com"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>"
   ```

### Microsoft

1. [Azure Portal](https://portal.azure.com/) → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. Supported account types: "Accounts in any organizational directory and personal
   Microsoft accounts" for the broadest reach.
3. Redirect URI: platform **Web**, `https://localhost:7105/signin-microsoft`.
4. **Certificates & secrets** → **New client secret** → copy the secret *value*
   (not the ID).
5. Set the secrets:

   ```bash
   dotnet user-secrets set "Authentication:Microsoft:ClientId" "<application-client-id>"
   dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "<secret-value>"
   ```

## What happens on sign-in

1. `Login.razor` renders one `<form>` per configured provider →
   `POST /Account/PerformExternalLogin` (`Endpoints/AccountEndpoints.cs`) issues
   the `Challenge`.
2. Provider authenticates the user → redirects to `/signin-<provider>` → the
   handler middleware → `Components/Account/Pages/ExternalLogin.razor`.
3. `ExternalLogin.razor`:
   - login already linked → sign in;
   - otherwise **auto-provision** a local `ApplicationUser` from the provider's
     verified email claim, link it, set `EmailConfirmed = true`, sign in.

   There is no email-confirmation step — the provider's verification is trusted
   (Google/Microsoft always; GitHub via `user:email`).

## Troubleshooting

| Symptom | Cause |
|---|---|
| No "Or sign in with" section at all | No provider configured — `ClientId` missing from config. Check `dotnet user-secrets list`. |
| Buttons appear after `dotnet user-secrets set` but only on restart | Config is read at startup; restart the app. |
| `redirect_uri_mismatch` from the provider | The registered callback URL doesn't exactly match the app origin + `/signin-<provider>` (scheme, port, trailing slash). |
| Works locally, fails in prod | Production redirect URIs not registered with the provider, or `Authentication__<Provider>__*` env vars not set on the host. |
| GitHub sign-in has no email | App missing the `user:email` scope, or the account has no verified email. |
