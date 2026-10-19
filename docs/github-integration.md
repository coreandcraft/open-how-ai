# GitHub integration: App setup and operations

This runbook covers registering the GitHub App, storing credentials, configuring openhow locally and on Azure, installing it, and operating it. The same abbreviated instructions appear in **Integrations → GitHub App setup and operations**.

The initial implementation used anonymous public API access. The server now also supports GitHub App installation authentication. **App mode is the recommended configuration for a shared deployment.** Public mode remains available for a small evaluation. This feature continues to support public repositories only.

This repository integration is separate from the GitHub Actions → Azure OIDC identity used to deploy openhow. Do not reuse an Azure Client ID or the deployment identity for these settings.

## 1. Register the GitHub App

Use an account or organization that will own and maintain the integration. In GitHub, open **Settings → Developer settings → GitHub Apps → New GitHub App**. [GitHub registration reference](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app).

Set the registration fields as follows:

- **Name:** a unique name, for example `openhow Source Links`.
- **Homepage URL:** your openhow origin, for example `https://openhow.azurewebsites.net`.
- **Callback URL:** leave blank.
- **Request user authorization (OAuth) during installation:** unchecked.
- **Device Flow:** unchecked.
- **Setup URL:** leave blank; users return to openhow manually.
- **Webhooks → Active:** unchecked. No webhook URL or secret is needed.
- **Repository permissions → Contents:** Read-only. Metadata read access is included.
- **Other permissions:** no access.
- **Where can this GitHub App be installed?:** Any account for a multi-user deployment. Only on this account works for an owner-only development App.

Click **Create GitHub App**. This implementation acts as an installation, so it needs neither an OAuth callback nor a client secret. GitHub supports disabling webhooks for authentication-only Apps. Use separate Apps and keys for development and production.

## 2. Record the identity and generate the key

On the App's settings page:

1. Copy the **Client ID**. The JWT issuer uses this value; no numeric App ID setting is needed.
2. Copy the **slug** from its public URL: `https://github.com/apps/<slug>`.
3. Under **Private keys**, click **Generate a private key** and retain the downloaded PEM in a restricted location outside the repository.

The PEM is a signing credential, not a personal access token or an OAuth client secret. Do not place it in a frontend variable, source control, application project records, chat, or deployment logs. [GitHub key management](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/managing-private-keys-for-github-apps).

## 3. Configure local development

Prerequisites: .NET 10 SDK and Node.js; Docker Desktop with Linux containers if using Aspire.

Run from the repository root in PowerShell. Replace the example values with the registered App's values:

```powershell
$env:GitHub__Enabled = 'true'
$env:GitHub__AuthenticationMode = 'App'
$env:GitHub__App__ClientId = 'YOUR_GITHUB_APP_CLIENT_ID'
$env:GitHub__App__Slug = 'YOUR_GITHUB_APP_SLUG'

$githubPemPath = 'C:\keys\openhow-development.private-key.pem'
@{
    'GitHub:App:PrivateKeyPem' = Get-Content -LiteralPath $githubPemPath -Raw
} | ConvertTo-Json | dotnet user-secrets set --project src/Journey.Server

./start.ps1
```

The key goes through stdin rather than being embedded in the command line. User-secrets are development storage, not encrypted production storage.

Open `http://127.0.0.1:5080` and sign in. The default Development demo account is `alex@journey.local` / `JourneyDemo!2026`. Existing workspaces are retained; migrations run through the normal startup process.

For Aspire, run `./start.ps1 -Aspire` in that same PowerShell session and open `http://localhost:5173`. The host forwards the four nonsecret settings. The server loads its own private key from its user-secrets; the key is not added to the Aspire dashboard environment by this integration.

To persist nonsecret settings across terminal sessions, set the same colon-delimited keys in server user-secrets, e.g. `dotnet user-secrets set 'GitHub:AuthenticationMode' 'App' --project src/Journey.Server`. A conflicting environment variable overrides local user-secrets. Restart after configuration changes. If Visual Studio already has the server running, restart that debug session to load the new backend.

## 4. Configure Azure App Service

Deploy the updated application through the existing workflow first. The [go-live checklist](go-live-checklist.md) remains the prerequisite for PostgreSQL, Blob Storage, HTTPS, account email, managed identity, and Key Vault.

The four **nonsecret App Service environment settings** are:

```text
GitHub__Enabled=true
GitHub__AuthenticationMode=App
GitHub__App__ClientId=YOUR_GITHUB_APP_CLIENT_ID
GitHub__App__Slug=YOUR_GITHUB_APP_SLUG
```

The **Key Vault secret name** is `GitHub--App--PrivateKeyPem` and its value is the full PEM text, including BEGIN/END lines. The existing Azure configuration provider maps double hyphens to colons, producing `GitHub:App:PrivateKeyPem`. The application already loads the vault configured by `KeyVault:Uri` using managed identity.

The provisioning operator needs permission to set that secret. The App Service identity needs **Key Vault Secrets User** on the app's vault; `infra/main.bicep` already grants that role. Do not put the PEM in GitHub Actions variables.

Example operator commands, after signing into the correct Azure subscription:

```powershell
$githubResourceGroup = 'YOUR_RESOURCE_GROUP'
$githubWebApp = 'YOUR_APP_SERVICE_NAME'
$githubVault = 'YOUR_APP_KEY_VAULT'
$githubPemPath = 'C:\keys\openhow-production.private-key.pem'

az keyvault secret set --vault-name $githubVault --name GitHub--App--PrivateKeyPem --file $githubPemPath --encoding utf-8 --output none
az webapp config appsettings set --resource-group $githubResourceGroup --name $githubWebApp --settings GitHub__Enabled=true GitHub__AuthenticationMode=App GitHub__App__ClientId=YOUR_GITHUB_APP_CLIENT_ID GitHub__App__Slug=YOUR_GITHUB_APP_SLUG --output none
az webapp restart --resource-group $githubResourceGroup --name $githubWebApp --output none
```

`--output none` prevents the CLI from printing secret values or the application's settings. See the Azure CLI references for [secret uploads](https://learn.microsoft.com/en-us/cli/azure/keyvault/secret#az-keyvault-secret-set) and [App Service settings](https://learn.microsoft.com/en-us/cli/azure/webapp/config/appsettings#az-webapp-config-appsettings-set).

For repeatable infrastructure deployments, persist these **Bicep parameter values** in the environment's parameter set:

```text
githubEnabled = true
githubAuthenticationMode = 'App'
githubAppClientId = 'YOUR_GITHUB_APP_CLIENT_ID'
githubAppSlug = 'YOUR_GITHUB_APP_SLUG'
```

Do not rely only on a portal edit: redeploying infrastructure with the default parameter values disables the feature. The PEM is provisioned separately in Key Vault, not supplied as a Bicep parameter. The deployment workflow does not register the App or generate its keys.

Allow outbound HTTPS from the server to `api.github.com` and keep the host clock synchronized. Users need browser access to `github.com`. There is no incoming webhook endpoint to expose. A configuration or secret change requires restarting **all** application instances; credentials are loaded at startup.

## 5. Verify credentials and install on a repository

1. Open **Integrations** in the intended workspace, using an owner, admin, or editor account.
2. Confirm the App section appears without a configuration error. Click **Check App credentials**. Success validates the signing credentials against `GET /app` and confirms the configured slug.
3. Click **Install or manage GitHub App**. On GitHub choose the account that owns the repository, select **Only select repositories**, and select the public repository.
4. Complete installation. Organization policy may require an owner to approve the installation or repository access. A pending request is not a completed installation.
5. Return to openhow. Choose or create an application project, enter the public repository URL, and choose a branch (blank selects its default).
6. Set **Project folder in tool calls** when calls use absolute paths. Example: `C:\_Dev\JourneyAI` maps `C:\_Dev\JourneyAI\src\example.ts` to `src/example.ts`. Use the folder recorded in the conversation, not a folder on the server.
7. Click **Link repository**. This validates installation access, public visibility, and the branch.
8. Open a conversation, expand **Source grouping & import history**, and choose the project under **Application project**.
9. Open a supported read-file call. Check its GitHub link and use **Preview source**. This last step validates the actual Contents permission and file/revision access.

A successful credential check does not prove that a particular repository was selected for installation. Link validation and a file preview are separate acceptance checks. App installation is permission to read the selected repository, not a login to openhow.

## 6. Acceptance checks and runtime behavior

A successful deployment should satisfy all of these:

- `GET /api/health` returns OK. This checks application availability only, not GitHub.
- While signed in, `GET /api/integrations/github/status` returns `enabled: true`, `mode: "App"`, `configured: true`, and the expected installation URL. It never returns a PEM, JWT, or installation token.
- **Check App credentials** succeeds. Its API is `POST /api/w/{workspaceId}/integrations/github/check`; it requires an editor role and the normal `X-Journey: 1` request header.
- An installed public repository can be linked; an uninstalled or private repository fails with an actionable message.
- A known file preview loads at the expected revision and line range.
- A viewer can read an authorized conversation but cannot change repository settings or run the administrator credential check.

The server signs short-lived RS256 JWTs, discovers the installation for the submitted repository, and requests an installation token restricted to that repository with Contents read permission. Tokens are cached in memory, up to 256 repository entries per server process, and refreshed on the next request two minutes before expiry. GitHub installation tokens expire after one hour. Tokens never enter the database or browser. [GitHub installation authentication](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation).

The server checks public visibility before every authenticated preview. Revoked/denied requests invalidate cached tokens; the next attempt rediscovers the installation. App authentication failures do **not** fall back to anonymous requests.

The preview fetches up to 256 KB of UTF-8 text and displays up to 160 lines. It starts at the recorded line when known. Requests are bounded by time and response size. File bodies are rendered in the browser but are not saved as imported conversation evidence.

No webhook processing, background synchronization, OAuth user account connection, or repository ownership verification is included. Repository selection is completed on GitHub and the URL is then linked in openhow. Private repositories and GitHub Enterprise remain unsupported.

## 7. Monitor and troubleshoot

Search application logs for `GitHub App request failed` and `GitHub repository request failed`. They record upstream HTTP status without credentials. Track the openhow GitHub routes' error rate and latency in Application Insights; user-visible upstream failures normally return an openhow 400 with a useful message, so monitoring only HTTP 500s will miss them.

- **Setup incomplete:** verify mode, Client ID, slug, and PEM in the backend configuration. Setting these only on Vite has no effect.
- **Invalid private key:** upload the complete private PEM, not the public key or a client secret. Real PEM line breaks are preferred; escaped `\n` is accepted.
- **App credentials rejected / upstream 401:** confirm that the Client ID and private key belong to the same App, the key has not been deleted, and the host clock is accurate.
- **Slug mismatch:** copy the slug from the registered App's URL; it may differ from its display name.
- **Installation not found / upstream 404:** install the App on the correct owner account and include the repository. Resolve any pending organization approval.
- **Missing Contents permission / upstream 403 or 422:** grant Contents read access on the App and have the installation owner approve the changed permissions.
- **Suspended or uninstalled App:** restore/reinstall on GitHub and retry. GitHub enforces revoked access; openhow does not bypass it.
- **Rate limit / upstream 403 or 429:** reduce repeated previews and retry after GitHub's limit resets. Investigate upstream `x-ratelimit-*` headers through approved server diagnostics, without logging Authorization headers or token response bodies. A normal file link remains usable.
- **Cannot reach GitHub / timeout:** check DNS, outbound HTTPS rules, proxy configuration, and GitHub service status.
- **File/revision not found:** verify the path, branch or recorded commit still exists. A failed pinned preview does not substitute a newer revision.
- **No source link:** ensure the conversation's project matches the connection, the structured tool is recognized, and any absolute path is under the configured folder.
- **Feature remains off after restart:** check effective configuration precedence. Key Vault is added after other providers in this application; a GitHub flag stored there can override an App Service environment value.

Public mode has GitHub's unauthenticated limit of 60 requests/hour per outbound IP. App installations generally start at 5,000/hour, with different enterprise/scaling rules and additional secondary limits. These are GitHub limits, not quotas guaranteed by openhow. [Current GitHub rate-limit rules](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api).

## 8. Rotate, revoke, or disable

For a normal private-key rotation:

1. Generate an additional private key in the same GitHub App.
2. Replace the PEM secret in Key Vault or local user-secrets.
3. Restart every openhow instance to load the key and clear cached tokens.
4. Run **Check App credentials**, then link/preview a known installed public repository.
5. Delete the old key from GitHub only after the replacement works.

GitHub permits overlapping keys for rotation. Deleting an old signing key is not a guarantee that already-issued installation tokens stop working immediately; for an incident, suspend/uninstall the affected installation and follow GitHub's revocation procedure. [Private-key lifecycle](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/managing-private-keys-for-github-apps).

To disable the feature, set `GitHub__Enabled=false` in the effective configuration and restart. This hides source integration context and rejects previews/new connections while preserving saved project mappings. Unlink is still allowed. **Unlink** clears only the openhow project connection; it does not uninstall the GitHub App. Manage installation removal in GitHub.

If switching deliberately to `GitHub__AuthenticationMode=Public`, restart and expect anonymous rate limits. A configuration error never switches modes automatically.

## Source-link semantics

Supported structured tool names are `Read`, `read_file`, and `readfile`, including qualified names. Paths come from `path`, `file_path`, `filePath`, or `target_file`. Shell command text is not mined for paths. Traversal and absolute paths outside the configured repository folder do not produce links.

Explicit line fields are honored. `Read` uses a one-based offset; Gemini CLI `read_file` uses a zero-based offset. Unknown conventions produce a file link without invented line numbers.

A valid full SHA in session metadata `git.commit_hash` pins the link and preview. If a recorded remote is present, it must match the linked repository. Otherwise the configured branch is used and labeled **Latest on GitHub / Original revision unknown**. Even a pinned commit can differ from local uncommitted changes. Original tool payloads remain intact.

One repository and recorded local root are supported per application project. Existing public share snapshots do not gain live connections; workspace sharing rules stay unchanged.

## Public-mode evaluation without an App

For a short local evaluation:

```powershell
$env:GitHub__Enabled = 'true'
$env:GitHub__AuthenticationMode = 'Public'
./start.ps1
```

No GitHub registration or key is used in this mode. Link a public URL directly through Integrations. This is the path described by the initial startup-only instructions, not the App setup above.

## Verification commands

```powershell
dotnet test tests -c Release
npm --prefix src/Journey.Client run build
npm --prefix src/Journey.Client run test:i18n
```

From `src/Journey.Client`, run `npx playwright test --config playwright.messages.config.ts`. Automated tests use a controlled GitHub substitute to verify JWT signatures, repository scope, caching, revocation, permission failures, public visibility, setup controls, and source previews. A live installation must be verified with the acceptance steps after real credentials have been provisioned.
