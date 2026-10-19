# Architectural decisions

## Source and curation are separate aggregates

Original file bytes, provider payloads and normalized messages are immutable through the public application API. Journeys reference conversation IDs instead of copying conversations. Curator prose, connections, annotations and comparison links belong to separate editable aggregates. The conversation parent graph describes source history; journey edges describe a human interpretation. Unknown provider fields survive normalization in metadata.

## Physical solution and logical features

`JourneyAI.sln` is the root solution. All application source lives under `src/`:

```text
src/
  Journey.Client/                 React + TypeScript + Vite (unchanged stack)
    src/app/                     Application shell, entry point and styles
    src/features/                Conversations, imports, journeys, review,
                                 comparisons, publications and workspaces
    src/shared/                  UI primitives, state and localization
  Journey.Server/                The only executable/deployment entry point
    Features/<Feature>/          HTTP endpoints and request contracts
    Program.cs                   Authentication, middleware and composition
    wwwroot/                     Generated client output; ignored by Git
  Journey.Service/               Reusable behavior and hosted workers
    Features/<Feature>/          Imports, anchoring, publication projection,
                                 authorization, collaboration and demo bootstrap
  Journey.Data/                  EF Core context, mapping and migrations
    Features/<Feature>/          Feature-owned model configuration
    Migrations/                  Existing migration history, preserved IDs
  Journey.Domain/                 Entities and value objects grouped by feature
    Features/<Feature>/Models.cs
tests/                          Backend regression tests (at repository root)
fixtures/                       Import samples (at repository root)
```

The build dependency chain is Server -> Service -> Data -> Domain. Domain has no framework or persistence dependency. Data owns SQLite/EF Core. Service references ASP.NET Core for the existing authorization context, SignalR hub and hosted worker, but never references Server. HTTP adapters stay in Server. The host composes services through `AddJourneyServices`.

This is a single deployable application with physical library boundaries and logical vertical slices. Work on a feature follows its matching folders across Client, Server, Service, Data and Domain. New use cases should stay inside that feature (for example `Features/Imports/Commit/`) as it grows. Do not create a project per endpoint, generic repositories, or empty service wrappers. Simple HTTP/database orchestration stays in its endpoint; reusable parsing, authorization and projection logic belongs in Service. Feature folders are not independently deployed services.

Server and Service namespaces include the feature name. Domain models share `Journey.Domain`; persistence configuration shares the partial `Journey.Data.AppDb`, with mappings physically grouped by feature. This keeps the EF model identity stable within the new assembly and centralizes the unit of work. Shared code is limited to cross-feature concerns such as identity, authorization, JSON serialization and client state.

React keeps lazy loading for the large reader/canvas modules. Vite builds directly into Server/wwwroot. The root solution includes the client as solution items; it remains an npm project and does not require an additional Visual Studio JavaScript SDK. A normal .NET build compiles the backend; `start.ps1` builds the client for local use and `dotnet publish src/Journey.Server` builds and packages both. Runtime traffic uses one origin: `/api`, `/hubs/collaboration`, static assets and the SPA fallback all belong to the ASP.NET Core host.

## Migration and compatibility

The previous `backend` directory moved to `src/Journey.Server`, and `frontend` moved to `src/Journey.Client`. The local database moved with Server. Existing migration IDs and database tables were retained; the model snapshot and designer namespaces now identify Domain models and the Data context. No new database migration was introduced by this restructuring. EF commands must specify Data as the target project and Server as the startup project:

```powershell
dotnet ef migrations has-pending-model-changes --project src/Journey.Data --startup-project src/Journey.Server
dotnet ef migrations add <Name> --project src/Journey.Data --startup-project src/Journey.Server
dotnet ef database update --project src/Journey.Data --startup-project src/Journey.Server
```

App Service is the initial deployment target. See [deployment](deployment.md) for the combined publish artifact and configuration. The current SQLite provider has been retained; selecting a managed production database is a separate decision.

## Persistence and stable identity

SQLite provides a zero-service local start. EF migrations, UUID identifiers, membership indexes and explicit source IDs make the data model portable to SQL Server. Imported bytes are BLOBs for prototype simplicity. Production should move these to immutable object storage without exposing storage references publicly.

Journey graphs are saved as an atomic JSON document with a concurrency token. This deliberately avoids partial graph saves and permits XYFlow data/viewport preservation. JourneyNode, JourneyEdge, JourneyReadingStep, AnnotationAnchor, CommentThread and PublicationSnapshot are modeled as value objects/aggregate representations, not independent EF tables. Normalize these later if database-level edge queries or very large graphs justify the complexity.

## Authentication and workspace authorization

Local salted password hashes and HttpOnly, SameSite=Strict cookies support multiple genuine users without cloud setup. Mutating API requests require a custom header; no permissive CORS policy is enabled. Membership is looked up for every authorized operation; client role labels are presentation only. Private journeys remain owner-only even in a team workspace. Team membership projection is non-authoritative; workspace memberships control access.

Invitations are random tokens bound to an email and expire after seven days. Owner/admin/editor/commenter/viewer capabilities are explicit. Editor publication is denied. User-facing content is rendered without raw imported HTML and remote images are not auto-fetched.

## Import jobs

A preview detects a supported adapter, normalizes source data and persists unchanged bytes. Commit records the chosen source fingerprints and queues a database-backed job. The single hosted worker resumes queued/processing batches, skips existing fingerprints and records progress/errors. ZIP entries are never extracted to filesystem paths. Expanded entry sizes and counts are bounded. There is no assumption of an official export contract for every provider.

Duplicate identity is provider plus exact per-conversation raw JSON and workspace. This favors fidelity and a clear audit trail over aggressive semantic deduplication. Import IDs and source IDs remain separate from provider identifiers. Preview and commit use the same adapters, so selections use content fingerprints rather than transient normalized UUIDs.

## Reliable annotation anchoring

An anchor stores block ID, UTF-16 offsets, exact quote, prefix/suffix and SHA-256 of UTF-8 source text. Fingerprint-matching offsets are checked first. Recovery uses surrounding context or an unambiguous quote; missing or ambiguous targets are orphaned. No fuzzy relocation attaches a comment to a convenient but incorrect passage. Markdown display can change visible offsets, so Source text mode guarantees exact selection of original characters. The source itself never changes when a comment is added.

## Collaboration and conflicts

SignalR authorizes workspace joins and broadcasts presence and invalidation events. REST remains the data/authorization boundary. A saved graph increments its version; stale writers receive HTTP 409. Clients with no local changes adopt remote saves, while clients with unsaved drafts retain their work. This is deliberately not a CRDT and does not broadcast every pointer movement. Production needs distributed presence, membership-revocation connection cleanup and a clearer merge UI.

## Publishing is a projection, not a visibility filter

Public endpoints have no path to authoring entities. The snapshot builder explicitly selects title/description, safe node fields, user-selected source messages, selected plain-text curator explanations and connections whose endpoints were included. Unknown JSON properties, raw payloads, tools, attachments and collaborator comments are absent. Exact redactions are applied server-side before persistence. Scanning traverses decoded text fields so suggested redactions match original strings. The author must review the snapshot and explicitly confirm publication.

A snapshot is immutable and stored independently of its source journey. New conversations and edits do not alter an existing publication. Random unlisted slugs are bearer links; public snapshots appear in the discovery endpoint. Attachment references embedded in prose remain reviewable source text; no binary attachment payload is exposed.

## Search and future desktop packaging

The first library slice performs database-backed substring search without external AI dependencies. A future search service can provide SQLite FTS5 or SQL Server full-text search, then optional semantic retrieval. Browser files enter through upload APIs; React has no native filesystem dependency. Tauri/Electron can host this UI with an authenticated local API and an explicit file bridge. Desktop distribution, updater/signing and offline multi-device synchronization are deferred.

## Primary references used

- React Flow API: https://reactflow.dev/api-reference/utils/add-edge
- React Flow quick start: https://reactflow.dev/learn
- ASP.NET Core antiforgery concepts: https://learn.microsoft.com/aspnet/core/security/anti-request-forgery

## Standalone sharing and decision comparisons

Conversation publications have a nullable JourneyId and a ConversationId. A dedicated projection selects only message IDs belonging to that conversation, applies redactions, and serializes an immutable standalone reader payload. No synthetic journey is persisted. Empty and foreign message selections are rejected; preview and publication require workspace publish permission, and publication requires explicit confirmation.

Comparison sessions can live directly in a workspace (JourneyId null). Optional journey associations retain the journey visibility check. Question, paired evidence, criteria, outcome, rationale and range filters persist independently of source conversations. Both linked messages are validated against the selected conversations and workspace. The comparison UI makes the curator-authored decision the deliverable, with direct links back to each supporting source. Existing comparison records remain readable after the additive EF migration.

## Workspace creation and parallel presentation

Workspace management is separate from member administration. POST /workspaces accepts Personal or Team; personal workspaces have no TeamId and reject invitations. A creator gets owner membership, while new teams also receive an owner TeamMember. Workspace IDs continue to scope every library and curation API.

Publication projects only finite numeric x/y coordinates from authored node positions, retaining parallel branch placement without exposing arbitrary graph metadata. Older snapshots without coordinates fall back to the original grid. Reading sequence remains independent from execution chronology; concurrency must be explained by the curator rather than inferred from import order.

Annotation publication is explicit per thread via AnnotationIds (empty by default). Endpoints load selected threads within the publication workspace and restrict journey-specific annotations to that journey; standalone conversation shares accept only conversation-level annotations. Projection requires each message/node target to be included and rejects orphaned annotations. It copies only target IDs, redacted quote, resolved state and redacted comment/reply text. Raw anchor context, comparison anchors, author identity and mention records remain private. Selected annotation text participates in the same preview, scan, redaction and immutable snapshot as source text.

## Private annotation artifacts

AnnotationArtifact metadata is associated with an annotation; ArtifactBlob stores unchanged bytes separately so thread responses never serialize file data. Read/upload endpoints enforce workspace membership, comment capability for uploads, and private-journey access where applicable. Allowed extensions must match basic content signatures. The request body limit includes multipart overhead while individual files are capped at 50 MiB. ZIPs are stored without extracting or executing their contents. Public projections do not include artifact records or bytes. The initial implementation stores blobs in SQLite; a future storage interface can move binaries to object storage without changing annotation ownership.

## Restructure verification

Validated on 2026-10-08 with the existing .NET 10 and React/Vite toolchain:

- All 45 backend tests pass before and after project extraction.
- Client TypeScript/production build and all 3 localization unit checks pass.
- All 12 browser scenarios pass (11 in the full run, with the affected localization suite rerun successfully after scoping its ambiguous language-picker selector to the header).
- EF reports no pending model changes after moving the context and migrations to Data.
- The actual Release publish output serves the API health endpoint, HTML entry point, JavaScript assets and SPA deep links from one host.
- A fresh production database receives all 3 existing migrations without demonstration accounts; local SQLite files are absent from the publish package.
- The local preview is restored against the original database in its new Server location.
