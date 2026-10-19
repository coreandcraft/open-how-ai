# Local development with Aspire

## Start the environment

Install .NET 10 SDK, Node.js with npm, and Docker Desktop using Linux containers. Start Docker Desktop, then run from the repository root:

```powershell
./start.ps1 -Aspire
# Equivalent direct command:
dotnet run --project src/Journey.AppHost
```

The AppHost restores frontend packages and runs Vite with hot reload. It starts the API after PostgreSQL is healthy and starts the frontend after the API has applied migrations and seeded demo data. The first run downloads container images; pgAdmin may take several minutes to become available. Use the dashboard login URL printed in the terminal to see resource status and console logs. The dashboard listens on `http://localhost:15888` and requires its generated login token.

Open `http://localhost:5173` and sign in with `alex@journey.local` / `JourneyDemo!2026`. Requests to the API, SignalR, MCP, and social-login callbacks go through Vite to the API's Aspire-assigned address. The public site URL is set to the frontend endpoint for email links. Backend changes require restarting the server resource or AppHost; Vite watches frontend changes automatically.

The original `./start.ps1` command still runs the standalone SQLite app on port 5080. PostgreSQL starts with a separate database and demo data; existing SQLite records are not copied or deleted.

## Inspect PostgreSQL

Open `http://localhost:5050`, or the pgAdmin endpoint in Aspire. Aspire preconfigures the PostgreSQL server. Expand **Servers → postgres → Databases → journey → Schemas → public → Tables** to inspect columns, indexes, foreign keys, and data. Use pgAdmin's ERD tool to review the relationships that are defined in the database.

For DBeaver, DataGrip, a desktop pgAdmin installation, or psql, connect with:

- Host: `localhost`
- Port: `5433`
- Database: `journey`
- Username: `postgres`
- Password: reveal the connection string on the `Journey` database resource in the Aspire dashboard.

Port 5433 keeps this database separate from an existing local PostgreSQL installation on its standard port 5432.

Aspire generates the PostgreSQL password. Its AppHost user-secrets ID keeps generated credentials stable between runs. To set your own password **before creating the volume**, use:

```powershell
dotnet user-secrets set "Parameters:postgres-password" "YOUR-LOCAL-PASSWORD" --project src/Journey.AppHost
```

PostgreSQL 17 stores data in a Docker named volume, so stopping AppHost and restarting it retains schema and records. Do not change the password parameter after initialization without also changing the database role password: PostgreSQL only applies its initialization password to an empty volume. Deleting the volume deletes that local database.

If ports are occupied, override the AppHost configuration:

```powershell
dotnet run --project src/Journey.AppHost -- --Ports:Postgres 5434 --Ports:PgAdmin 5051 --Ports:Client 5174
```

## Update the schema

Both providers use `Journey.Data.AppDb`. SQLite migrations live in `src/Journey.Data/Migrations`; PostgreSQL migrations and its snapshot live in `src/Journey.Data.PostgreSql/Migrations`. Generate a migration for **each provider** when changing the model:

```powershell
dotnet tool restore
dotnet ef migrations add YourChange --project src/Journey.Data --startup-project src/Journey.Server -- --Database:Provider Sqlite
dotnet ef migrations add YourChange --project src/Journey.Data.PostgreSql --startup-project src/Journey.Server -- --Database:Provider PostgreSQL --ConnectionStrings:Journey "Host=localhost;Database=journey;Username=postgres"
```

Scaffolding does not require a running database or password. Review generated migrations, then restart the app to apply them. The PostgreSQL baseline represents the current model, including accounts, imports, publications, and experimental MCP integration. It does not replay SQLite-specific historical SQL. Substring search indexes (`pg_trgm`) are a PostgreSQL-only SQL migration (`SearchTrigramIndexes`); do not add a matching SQLite migration.

To use an independently managed PostgreSQL database, set `Database__Provider=PostgreSQL` and `ConnectionStrings__Journey` on `Journey.Server`. Without a provider setting the server uses SQLite. Unknown provider names fail explicitly.

Hosting integration references: [Aspire PostgreSQL](https://aspire.dev/integrations/databases/postgres/postgres-host/) and [Vite integration](https://aspire.dev/integrations/frameworks/javascript/).

## Keep private runtime data out of Git

Install Python 3.9 or newer and activate the repository's hooks in every new clone:

```powershell
git config core.hooksPath .githooks
python tools/check-repository-privacy.py --staged
```

The commit hook checks the complete staged snapshot. The push hook checks the full history of each outgoing ref, including files deleted by later commits. Both refuse the operation if the check cannot run. They reject database files and journals, private import storage, local environment and secret configuration, private keys, development mail, generated test reports, and recognizable credentials. `.env.example`, `.env.sample`, and `.env.template` may contain placeholders only; their contents are still scanned. SQLite content is also detected when renamed.

For an intentional synthetic connection-string credential in C# source under `tests/` or `tools/Journey.*Bench/`, append `// privacy: synthetic-credential=VALUE` to the same line, using the exact fixture value (including interpolation braces). This exempts only that value on that line. Other credentials, private files, and token/key patterns remain checked; the marker has no effect in production source or data fixtures.

Keep runtime databases and private backups in ignored local storage. Never use `git add -f` or `--no-verify` to bypass these protections. CI runs the same full-history check on pushes and pull requests, and the deployment workflow checks it before building. Local hooks are opt-in and can be bypassed; configure the `privacy` check as required in GitHub branch protection to enforce it for merges. Pattern scanning cannot identify all private prose or unknown credential formats, so review fixtures and attachments before staging them.

After a sensitive-history rewrite, use a fresh clone, enable the hooks again, and transfer only reviewed uncommitted source changes. Do not merge an old branch or restore an old Git bundle into the public repository: that can reintroduce removed history. Backups from before cleanup still contain sensitive data and must remain private. Credential rotation is a separate step from removing files.
