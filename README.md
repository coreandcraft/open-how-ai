# JourneyAI
Shared your AI building journey end to end, received feedback, remix.

## Run locally

For the full development environment, install .NET 10 SDK, Node.js, and Docker Desktop (Linux containers), start Docker, then run `./start.ps1 -Aspire`. Aspire starts PostgreSQL, pgAdmin, the API, and the Vite frontend with hot reload. Open the dashboard URL printed in the terminal, or the app at http://localhost:5173. PostgreSQL is available at `localhost:5433`, database `journey`, user `postgres`; pgAdmin is at http://localhost:5050. See [local development with Aspire](docs/local-development.md) for passwords, schema inspection, persistence, and migration commands.

Install .NET 10 SDK and Node.js, then run `./start.ps1`. The app runs at http://127.0.0.1:5080 with SQLite and no external services. Demo login: `alex@journey.local` / `JourneyDemo!2026`.

For manual startup: `npm --prefix src/Journey.Client install`, `npm --prefix src/Journey.Client run build`, then `dotnet run --project src/Journey.Server`. Migrations run on startup; existing workspace data is retained.

## Accounts, email, and deployment

Production targets Azure App Service with PostgreSQL, private Blob Storage, and Key Vault secrets loaded by managed identity. The [go-live checklist](docs/go-live-checklist.md) covers provisioning, settings, GitHub OIDC, data migration, rollout, and recovery. The deployment workflow is [deploy-azure.yml](.github/workflows/deploy-azure.yml), with Azure resources defined in [main.bicep](infra/main.bicep).

Accounts use ASP.NET Core Identity with confirmed email, five-attempt lockout, password recovery, authenticator MFA, and single-use recovery codes. Register, open the confirmation email, then sign in. Existing non-demo users must confirm their address using **Resend confirmation email**; their IDs, password hashes, and workspace access are preserved by the migration. Existing sessions need a fresh sign-in. Password resets invalidate existing sessions. Manage MFA under **Settings → Account security**. Social-only accounts can set a password through the reset flow before configuring MFA.

In Development, when no SMTP host is configured, emails are written as `.eml` files under `src/Journey.Server/data/mail` (open these in a mail client). They contain real account tokens and are excluded from git and publication. `Email:PickupDirectory` can override this directory. Demo accounts are only seeded and advertised in Development or Testing, and can be disabled with `SeedDemoData=false`.

Production requires `Site:PublicUrl` (the HTTPS origin, e.g. `https://your-domain.example`), `Email:Host`, and `Email:From`. Set `Email:Port` (default 587, STARTTLS), `Email:Username`, and `Email:Password` for your mail service. Verify the sending domain with your email provider. Put credentials in deployment secrets or local .NET user-secrets, never committed settings; environment variables use double underscores, such as `Authentication__Google__ClientSecret`. Configure `AllowedHosts` for the deployment domain. Terminate HTTPS correctly and configure trusted forwarded headers in your hosting environment if a reverse proxy terminates TLS. Persist ASP.NET Data Protection keys across deployments and share them across replicas so confirmation links, reset links, and sessions remain usable.

Google login is enabled when both `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are set. Register the exact callback `https://your-domain.example/signin-google` with Google. Facebook login requires `Authentication:Facebook:AppId` and `Authentication:Facebook:AppSecret`, with `https://your-domain.example/signin-facebook` registered as a valid OAuth redirect URI. Configure the providers' production consent/app access as required. Buttons appear only for configured providers. New social accounts verify their email with openhow; an existing account is never automatically linked merely because a provider reports the same email. Sign in using the original method when an address is already registered. MFA also applies to social sign-in.

The landing page includes server-delivered descriptive content, canonical URL, Open Graph/Twitter metadata, a share image, and WebSite structured data. Production `robots.txt` and `sitemap.xml` publish the landing page only; account/workspace/shared-link views remain excluded from indexing. Development and Testing are marked `noindex`. The favicon, touch icons, and manifest use the existing openhow brand. `tools/generate-brand-assets.ps1` regenerates the PNG assets on Windows.

Account integration tests cover confirmation, reset/session revocation, lockout, MFA/recovery codes, migration of legacy data, and Google/Facebook callbacks with stubbed provider responses. Browser account tests also read the real local pickup emails: `npx playwright test accounts.spec.ts` from `src/Journey.Client`. To test an isolated local server, set `JOURNEY_TEST_URL` and `JOURNEY_TEST_MAIL` to its URL and absolute pickup directory. Live OAuth consent and SMTP delivery must still be verified with the deployment credentials.

## Universal imports

The import wizard detects supported provider files, previews conversations and supplied assets, and associates records with a cross-provider project. Source history supports reviewing changed messages, accepting revisions while retaining earlier versions, recovering comment anchors, inspecting tool relationships, and downloading privately linked files.

See [import compatibility and configuration](docs/import-compatibility.md) for supported formats, provider limitations, optional consent-based local discovery, source encryption and retention, streaming limits, and adapter extension points. [Architecture](docs/architecture.md) and the [import integration plan](docs/universal-import-plan.md) describe the separation between source evidence and editable curation.

Run backend tests with `dotnet test tests`. With the app running, run browser tests with `npx playwright test` from `src/Journey.Client`. Generate reproducible synthetic scale fixtures with `node tools/generate-import-fixtures.mjs 10000 artifacts/generated-conversations.json` (choose a new output filename if it already exists).

## Build a journey from conversations

In **Conversations**, select conversations (including across filters or loaded pages), choose **Build journey**, enter a title, and create the editable draft. The new journey is private and opens in the canvas editor, where you can rename, move, group, connect, disconnect, undo, and save nodes as usual.

The builder orders source timestamps from left to right with separate conversation/thread lanes and tool-execution lanes. Overlapping tool executions stay in separate lanes; a lane is reused only when recorded start and end times establish that executions are sequential. Spacing shows chronological order, not elapsed duration. Unknown start times appear after dated activity and are labelled explicitly; import time is never substituted for source time.

Connections come from recorded parent-session/source relationships and tool execution records. Unselected or unresolved targets are reported without inventing links. Tool nodes retain references to the recorded call/result and open their source conversation. No AI inference or external model calls are used. The current limit is 100 selected conversations and 500 recorded tool executions per draft; larger selections return an error without creating a partial journey.

## GitHub source links (experimental)

Connect a public repository to an application project to add file and line links to supported read-tool calls, with optional source previews. Choose GitHub App installation authentication for shared deployments, or anonymous public access for evaluation. See [GitHub App setup and operations](docs/github-integration.md) for registration fields, permissions, local secrets, Azure Key Vault, installation, verification, rotation, and troubleshooting.

## Public discussions

Each new shared conversation or journey link has an **Allow comments & questions** switch, off by default. The person who creates the link can change it in the sharing dialog's link history or on the shared page. Registered users must sign in to post a comment, question, or reply; workspace membership is not required for a shared-link discussion. Visitors can read enabled discussions. Names and contributions are public to anyone with the link.

Discussion threads are stored separately from the immutable publication snapshot. Turning discussion off hides its threads and blocks new posts; re-enabling restores them. Revoking the shared link removes its discussion. Older links without recorded creator identity stay closed: create a new shared link to enable discussion. Sign-in and registration links return participants to the shared page.
