# ASP.NET Core hosting and Azure App Service

## Build one application

The ASP.NET Core Server project serves the React frontend, REST API and SignalR from one origin. React remains a Vite/npm application in `src/Journey.Client`; Node is needed during build, not at runtime.

From the repository root:

```powershell
dotnet test JourneyAI.sln
dotnet publish src/Journey.Server -c Release -o artifacts/publish
```

The publish target runs `npm ci` and `npm run build`, then includes the current client output under `wwwroot`. Deploy the **contents** of `artifacts/publish` to one Azure App Service configured for a compatible .NET 10 runtime, or publish for the selected platform with a self-contained runtime. The entry assembly is `Journey.Server.dll`. Publish Server, not the entire solution. Local database files are excluded from the package.

## App Service configuration

The production deployment is Azure App Service on Linux with PostgreSQL, Azure Key Vault, and private Azure Blob Storage. See the [go-live checklist](go-live-checklist.md) for provisioning, GitHub OIDC credentials, required settings, rollout, and rollback.

- `.github/workflows/deploy-azure.yml` tests, publishes the API/frontend, writes nonsecret production settings into the package, authenticates with GitHub OIDC, deploys, and checks health. Configure the protected GitHub `production` environment before enabling deployments.
- `infra/main.bicep` defines a single-instance Linux App Service plan/app, managed identity, private blob containers, an RBAC Key Vault, a wrapping key for data-protection keys, PostgreSQL Flexible Server 18 (`Standard_B2s`, 32 GB, `azure.extensions=PG_TRGM`), a 30-day Log Analytics workspace, and workspace-based Application Insights. It writes `ConnectionStrings--Journey` to Key Vault and sets `APPLICATIONINSIGHTS_CONNECTION_STRING` on the app. The deploy workflow also publishes an idempotent PostgreSQL migration script as the `postgresql-migrations` artifact. Startup still applies pending migrations unless `Database:MigrateOnStartup` is `false`.
- The same template creates warning alerts that email `felipe.ramos@fitcode.io` (override with `alertEmail`) when the 15-minute average goes above 80% for App Service plan CPU or memory, or PostgreSQL CPU, memory, or storage. Azure sends a confirmation to that address before any alert mail is delivered. Redeploy the template to create the alerts.
- The server uses Azure Monitor OpenTelemetry (`Azure.Monitor.OpenTelemetry.AspNetCore`) when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Production refuses to start without it. Local development and tests leave it unset, so they do not send telemetry. Request traces drop `/api/health`, query strings, and invitation tokens. Redeploy the Bicep template before a build that requires the setting.
- `appsettings.Production.json` holds nonsecret configuration. The Key Vault provider loads secrets using managed identity before application services are configured; vault secret names use `--` for configuration nesting (for example, `ConnectionStrings--Journey`). Do not put database, SMTP, OAuth, or encryption credentials in the workflow or JSON files.
- New uploads, raw import sources, extracted source files, and annotation attachments use the private `files` blob container. Data-protection keys use a separate container and Key Vault envelope encryption. Published JS/CSS/brand assets remain part of the versioned deployment package. Multipart buffers and parser scratch files are temporary, not durable storage.
- Remain on one App Service instance: import scheduling and SignalR presence are still in-process. Multi-instance deployment needs distributed worker coordination and a SignalR backplane/service first.
- Startup applies the provider-specific PostgreSQL migrations. Take a database backup, inspect migrations before deployment, and allow restart downtime. This workflow is a direct deployment, not a zero-downtime slot-swap pipeline.
- The Bicep definition enables HTTPS Only, Always On, WebSockets, health checks, and forwarded-header integration for App Service's managed reverse proxy. Do not reuse its broad cloud forwarded-header setting on an arbitrary internet-facing host; there, explicitly configure trusted proxies.

No Azure resources are created merely by checking in these files. Azure names, region, PostgreSQL networking, DNS, SMTP service, and GitHub federation must be configured for the target environment.

## Local development

Run `./start.ps1` from any working directory to install/build the client and start Server on the existing launch-profile port 5080. `./start.ps1 -SkipBuild` reuses an existing client build. For hot reload, start Server and run `npm run dev` inside `src/Journey.Client`; Vite proxies API, OpenAPI and SignalR requests to the Server. Local seeding and the existing SQLite data continue to work as before.

## References

- [Microsoft: Deploy ASP.NET Core to Azure App Service](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/azure-apps/?view=aspnetcore-10.0)
- [Microsoft: Configure ASP.NET Core apps in App Service](https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore)
- [Microsoft: ASP.NET Core SPA publishing](https://learn.microsoft.com/en-us/aspnet/core/client-side/spa/intro?view=aspnetcore-9.0)

## Conversation import quotas

Conversation imports have a **15 GB** default quota per batch. Operators can explicitly enable **20 GB** for selected user IDs in server configuration:

```json
{
  "Imports": {
    "LargeImportUserIds": ["user-id-to-enable"],
    "StoragePath": "D:/JourneyData/import-sources"
  }
}
```

The equivalent environment variable for the first enabled user is `Imports__LargeImportUserIds__0`. Use the stable `id` returned by `/api/me`; workspace owner/admin roles do not grant this feature. The default allowlist is empty. Remove the ID to revoke the tier for subsequent requests. Configuration file reloads apply to subsequent requests; environment-variable changes require a service restart.

`GET /api/imports/limits` returns the authenticated user's effective quota. Browser validation uses that result; the server independently resolves the requesting user's quota before reading the multipart body. GB is decimal: 2,000,000,000 or 20,000,000,000 bytes. The quota counts original uploaded files plus extracted archive contents across the batch. The 500-file/archive-entry rules, traversal checks, and prohibition on nested archives remain. The previous 20 MB archive-entry cap is removed.

Raw sources and archive entries stream into private Azure blobs in Production (`Storage:ContainerUri`); local development uses `data/import-sources`, overridable with `Imports:StoragePath`. Back up blobs with the database. Source downloads remain workspace-authorized and support range requests. Existing SQLite-backed sources remain readable. Failed previews clean up their newly written files. Provision temporary multipart-buffer space as well as durable storage, and configure upstream upload limits and timeouts to accommodate the enabled tier plus multipart overhead.

The quota is for a batch, not a promise that every single 20 GB structured document can be parsed. Adapters still materialize one structured source at a time; documents must fit the parser's memory/array limits. Large archives stream to disk and can contain multiple smaller exports. Full 20 GB end-to-end load testing is still required for a production capacity claim.

Annotation attachments retain their separate **50 MiB per-file** limit; the import feature grant does not increase it.

## Experimental MCP integration

The **Integrations** screen manages Model Context Protocol access. The implementation uses the official `ModelContextProtocol.AspNetCore` **2.2.0** SDK and ASP.NET Core JWT bearer authentication **10.0.12**, on the existing .NET 10 runtime. It exposes a stateless **Streamable HTTP** endpoint at `/mcp`; the SDK owns initialization, version negotiation, tool schemas, JSON-RPC, and protocol errors. There is no custom protocol, legacy SSE endpoint, stdio process, MCP approval extension, or OAuth authorization server in this application.

`Mcp:Enabled` defaults to `false`, and is enabled in Development settings. Changing registration settings requires restarting the server. When disabled, `/mcp` returns 404 and creation/review actions are disabled; existing connections can still be revoked. Database migrations add connection grants and proposal records.

### Local connection

1. Run the server using the Development launch profile on `http://127.0.0.1:5080`.
2. Sign in, open **Integrations**, choose **Add connection**, select specific journeys, and optionally permit proposals. Viewer and Commenter roles cannot enable proposals.
3. Save the credential displayed once. In a compatible MCP client, select Streamable HTTP, set URL `http://127.0.0.1:5080/mcp`, and set header `Authorization: Bearer <credential>`. Client configuration formats differ; the screen displays connection settings, not a universal client-specific config file.

Local credentials are accepted only on loopback requests in Development (or the automated Testing environment). The server stores a SHA-256 digest, never the credential. Connections expire after 30 days and can be revoked immediately for subsequent requests. Credentials and AI source content must not be placed in URLs or access logs. Native clients normally send no Origin header; unexpected browser origins are rejected. If a trusted client supplies an Origin header, list the exact origin in `Mcp:AllowedOrigins`. Cross-origin browser CORS access is not enabled by this feature.

### Authenticated remote connection

Remote access is a protected OAuth resource server and requires an external OAuth/OIDC authorization server with discovery, signed JWT access tokens, resource-specific audiences, authorization-code/PKCE support, and client registration appropriate to the clients being used. Configuring an issuer URL alone does not configure that provider or make an arbitrary MCP application compatible. Browser login cookies are never accepted at `/mcp`.

Example operator configuration (replace every example value):

```json
{
  "Mcp": {
    "Enabled": true,
    "Authority": "https://identity.example.com",
    "Resource": "https://openhow.me/mcp",
    "SubjectBindings": [
      { "Subject": "issuer-stable-user-subject", "UserId": "openhow-stable-user-id" }
    ],
    "AllowedOrigins": []
  }
}
```

`Authority` must match the token issuer exactly, including any trailing slash. `Resource` must be the externally reachable HTTPS MCP endpoint and the exact access-token audience. Production refuses to start with MCP enabled but without both HTTPS values. JWT discovery/signing keys are obtained from the configured trusted issuer; signatures, issuer, audience, expiration, and local subject bindings are checked. Tokens issued for unrelated APIs are rejected. Do not forward upstream service tokens to openhow.

The signed access token must contain:

- `sub`: an external subject explicitly mapped by the operator to an existing local user ID (see `/api/me`). Email matching is deliberately not used.
- `openhow_connection_id`: a grant created by that same user in **Integrations → Remote OAuth**. The provider must securely bind this grant to the authenticated subject; never copy an unvalidated client-supplied grant or user ID into a token.
- `scope`: space-delimited `mcp:read`, with `mcp:propose` additionally required for proposal calls. The local grant must also permit proposals.

The SDK publishes OAuth protected-resource metadata at `/.well-known/oauth-protected-resource/mcp` and advertises it in the POST endpoint's `WWW-Authenticate` challenge. Compatible clients discover the external authorization server there. Remote grants have no locally generated bearer secret. The operator must configure the issuer's claims, audience/resource handling, client registration, and subject bindings before users can connect. This repository does not provision an issuer or deploy a remote endpoint.

Serve both MCP and metadata over HTTPS. When TLS terminates at a reverse proxy, configure ASP.NET Core forwarded headers for **explicitly trusted proxy addresses** so `Request.IsHttps` is correct before the MCP boundary middleware. Do not blindly trust forwarded headers from arbitrary clients. Restrict `AllowedHosts` to your deployment hostnames. MCP requests have a 64 KiB body limit and a per-IP limit of 120 requests/minute remotely (600 in local development). MCP responses use `Cache-Control: no-store`; the UI is marked experimental.

### Tools and authorization

The six read tools are `search_journeys(query)`, `get_journey(journeyId)`, `get_journey_timeline(journeyId)`, `get_journey_evidence(nodeId)`, `get_related_conversations(conversationId)`, and `search_decisions(query)`.

Every call rechecks the user/security stamp, account lockout, grant expiration/revocation, current workspace membership and role, selected journey IDs, and private-journey ownership. Team access follows the project's existing team-workspace membership model; no organization-wide access is implied. A connection cannot select journeys outside its workspace. Removing workspace membership or resetting a user's security stamp invalidates access. Downgrading a role removes proposal permission on subsequent calls. Journey visibility is checked again on every call.

Read responses are curated and bounded: searches return at most 50 matches, journey/timeline reads at most 200 nodes, relationships at most 400 edges, and evidence at most 100 messages with 8,000 characters of text/code per message. Raw exports, attachment bytes, tool blocks, and hidden metadata are excluded. Nodes with references outside the journey workspace are excluded. `get_journey` returns qualified node IDs (`journeyId:nodeId`); use those for evidence, annotation, and relationship calls. Ambiguous unqualified IDs are rejected. The timeline reflects **curated reading order**, not inferred execution chronology. Related conversations follow explicit graph edges. Search is case-insensitive text matching, not semantic search. Source excerpts remain untrusted data for the AI client.

The four proposal tools are:

- `propose_journey_entry(data)`: `data` contains `journeyId`, `title`, optional `body`, `kind` (`note`, `decision`, or `conversation`), and optional `conversationId` (required for a conversation entry).
- `suggest_relationship(sourceId, targetId)`: two qualified node IDs in the same journey.
- `propose_annotation(nodeId, text)`: a qualified node ID and plain text.
- `propose_milestone(journeyId, data)`: `data` contains `title` and optional `body`.

Each creates a persisted pending proposal and returns its ID and base journey version. A human editor must review its plain-text content and explicitly confirm approval or rejection in the signed-in browser. The browser management API requires the application cookie and same-origin request header; an MCP token cannot approve or expand its own grant. There is no approval tool. Approval rechecks reviewer and proposer permissions, the connection, referenced data, and the exact journey version, then atomically changes the graph/annotation and records the reviewer. Duplicate reviews and stale proposals fail with HTTP 409. Reject stale proposals and request new ones. Proposal text is HTML-encoded when inserted into rich-text nodes. Approving a proposal never publishes or changes an existing published snapshot.

The review list prioritizes the oldest pending proposals, showing up to 100 records. Reviewed records remain in the database as an audit trail. MCP proposal calls are deliberately non-idempotent: if a client repeats an uncertain request, it may create another pending proposal, which must be reviewed separately.

Official references: [C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), [MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports), [MCP authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).

## Optional homepage links

Set `Site:JourneyUrl` to the public journey showing how openhow was built, and `Site:RepositoryUrl` to the repository's public web URL. Each homepage link appears independently when configured; the entire section is hidden when both are empty. Defaults are empty.

```json
"Site": {
  "JourneyUrl": "https://openhow.me/#public/your-published-journey",
  "RepositoryUrl": "https://github.com/your-account/your-repository"
}
```

Environment variables: `Site__JourneyUrl` and `Site__RepositoryUrl`. HTTPS URLs are supported; journey links can also use `/#public/your-published-journey`. Use a repository web URL rather than a local filesystem path or Git SSH clone URL. Invalid links are hidden. Only these two public values are exposed by `/api/site/links`. Setting a journey link does not publish the journey; publish it first. File configuration changes appear on the next homepage load; restart after changing environment variables.
