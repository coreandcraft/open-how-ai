# Production scaling decision: JourneyAI

Assessment date: October 10, 2026 (phases 0–3 October 9; local phase 4/5 October 10). Recommendation: launch on a properly bounded PostgreSQL + Blob application (option A) with a database-lease import queue. Move to independently deployed workers and read projections (option B) when a measured trigger fires, not up front. The local pilot pin is in `docs/performance/release-envelope.md`. Azure soak and canary are not done.

## What is validated, and what is not

A new benchmark runner executed actual authenticated ASP.NET endpoints against a separate local PostgreSQL database. It made **384 measured reads** across eight routes, two data sizes, and concurrency 1/4, with zero non-200 responses. It reproduced slow long-conversation reads and oversized journey/comparison responses. Passing these requests is not a production capacity certificate.

At the larger data size (1,000 regular synthetic conversations plus one long conversation, 21,000 synthetic messages, 100 synthetic journeys, 1,000 synthetic comparisons; existing demo seed adds a small number of records):

- The 1,000-message conversation returned 1,500,905 bytes. Median read time was **295 ms at concurrency 1** and **982 ms at concurrency 4**. Roughly 9.6 MiB was allocated per request across the server and in-process client.
- The journey list returned **6,724,359 bytes (6.41 MiB)** for 100 synthetic journeys, each containing 100 nodes. At concurrency 4 its median was 106 ms, with roughly **26.2 MiB allocated per request**. A fast local response still creates substantial network and browser work.
- The comparison list returned **2,320,413 bytes (2.21 MiB)**, despite being a list screen.
- A no-match text search rose from **6.3 ms** at the smaller stage to **233 ms** at the larger stage, at concurrency 1. This is a warning about query work, not a forecast that every future 10x increase will have the same multiplier.
- A 2 MiB public snapshot was read from PostgreSQL on every request; there is no measured cache/CDN hit path here.
- Throughput on large responses barely rose with concurrency (the long conversation went from 3.37 to 4.08 requests/second between concurrency 1 and 4), and no-match search grew about 37x for 7x more messages. Phase 0 below explains both.

Method of this first run: Release .NET 10, real routes via ASP.NET TestServer, local PostgreSQL 17, one warm-up per cell, 12 measured requests per cell, closed-loop concurrency. The reported p95 is effectively the maximum of 12 samples and is not statistically robust. Allocations are process-wide, include client buffering/serialization, and are not resident memory per request. Working set is a sample, not a peak measurement. Synthetic text is repetitive/compressible, which favors caching/storage compression. Workload stages add data without clearing OS/database caches. Other desktop workloads were not isolated.

Excluded: WAN/TLS/compression, actual browser rendering, Azure App Service/Blob/Key Vault, sustained arrival rates, cold starts, import/write contention, large graph rendering, MCP protocol traffic, publication generation, AI inference, context-quality evaluation, failover and multiple instances. The production large-project requirement remains **unvalidated** until the release gates below pass.

Raw results: `docs/performance/scale-baseline-2026-10-09.json`. Runner: `tools/Journey.ScaleBench`.

### Phase 0: what the slow and flat results actually were

The runner was rebuilt to give trustworthy numbers: Kestrel over loopback HTTP instead of TestServer, a client that streams responses so process allocations are dominated by the server, server garbage collection, five warm-ups and 100 samples per cell, concurrency 1/2/4/8, CPU time, garbage-collection pauses, database query count per request, `ANALYZE` after each seeding stage, and `EXPLAIN (ANALYZE, BUFFERS)` of the slowest query per route. It ran against a disposable PostgreSQL 17 container. Results: `docs/performance/scale-phase0-before-*.json` and `scale-phase0-before-plans-2026-10-09.txt`.

- **The long-conversation cost was the raw payload, repeated in every row.** `Include(Messages).ThenInclude(Blocks)` runs as one joined query, so the conversation's columns, including the 64 KiB `RawPayload`, were sent once per message: about 64 MiB per request for 1,000 messages. PostgreSQL executed the query in 5.6 ms; the time was spent moving and parsing that data. It cost 90–270 ms of CPU per request and capped throughput near 29 requests/second at concurrency 8. Real imports keep much larger raw payloads, so production would be worse than the benchmark.
- **The garbage collector was not the cause.** Runs under server and workstation garbage collection were within desktop noise of each other, with no consistent winner.
- **The 233 ms search was mostly stale statistics.** After `ANALYZE`, no-match search takes about 35 ms at the larger stage: a sequential scan of 21,000 blocks (17 ms) plus about 14 ms of PostgreSQL JIT compilation, triggered by an inflated cost estimate from the correlated subqueries. Search still scans every block and grows linearly; phase 2 must still add an index. Check `jit` on the production server and run `ANALYZE` after bulk imports.
- **Every authenticated request runs three baseline queries:** two for cookie security-stamp validation (configured to run on every request) and one workspace-membership lookup. That is a fixed cost to account for in capacity planning, and a candidate for short-lived caching later.

Run-to-run noise on the desktop is roughly ±50% at concurrency 1 for routes under 20 ms; compare large effects only.

### Phase 1 results

After the phase 1 changes (`docs/performance/scale-phase1-after-2026-10-09.json`), at 1,000 conversations:

- Long conversation: median 73 → 18 ms at concurrency 1 and 267 → 39 ms at concurrency 8; throughput 29 → 198 requests/second at concurrency 8, now scaling with concurrency; CPU 137 → 27 ms per request. The response shrank only from 1.50 to 1.44 MB because the synthetic raw payload is small; the gain comes from no longer transferring it per row.
- Public snapshot: served with one ID lookup plus one column read instead of a tracked entity, and repeat visitors get a 304 without the body.
- No failures in any cell. Lists (library, journeys, comparisons) are unchanged by design; they are phase 2.

### Phase 2 and 3 results

After the bounded-read and import-lease work (`docs/performance/scale-phase23-after-2026-10-09.json`, 20 samples per cell), at 1,000 conversations:

- **Library:** 291 KB → 14 KB (a page of 50). Median 4.8 ms at concurrency 1; 1,301 requests/second at concurrency 8. Query count stays at the 3-query baseline.
- **Journey list:** 6.41 MB → 107 KB (50 summaries with a 16-node preview). Median 3.4 → 4.5 ms; 1,368 requests/second at concurrency 8. Slightly over the 100 KB list target because each preview still carries positions and titles; drop `MaxNodes` if a later corpus exceeds it.
- **Comparisons:** 2.21 MB → 4.7 KB. Median 4.1 ms; 1,928 requests/second at concurrency 8.
- **Long conversation:** the route now returns the first 200 messages (287 KB, under the 512 KB page budget) instead of all 1,000 (1.44 MB). Median 18 → 8.7 ms at concurrency 1 and 39 → 13 ms at concurrency 8; throughput 198 → 496 requests/second.
- **Search-miss:** 35 ms → 5.9 ms. `EXPLAIN` uses `IX_MessageContentBlock_Text_trgm` (1.7 ms bitmap scan, 0 rows). Search-hit is 18 ms at concurrency 1 and 27 ms at concurrency 8 — still a substring match, now index-backed.
- No failures. Query count on lists does not grow with workspace size (also asserted in `ScalePagingTests`).

### Phase 4 results (local substitute) — October 10, 2026

Azure 30-minute / 2-hour soak, Blob, failover and rolling deploy were **not** run. The local substitute used the ScaleBench `pilot` profile (zipf sizes, mixed-entropy text, 1,000-node journey, skewed second workspace) on loopback Kestrel against a disposable PostgreSQL 17 container. Raw: `docs/performance/scale-phase4-pilot-2026-10-10.json` and the two `scale-phase4-browser-*.json` files.

At 1,541 conversations / 25,390 messages / 16 journeys (15 closed samples per cell):

- **Library** 14.5 KB, p50 4.4 ms (c=1). **Journey list** 64 KB, p50 3.7 ms. **Comparisons** 4.7 KB. **Summary** 185 bytes.
- **Long conversation** (first page) 120 KB, p50 12 ms. **1,000-node journey** 266 KB, p50 4.4 ms (c=1) and 11 ms (c=8).
- **Search-hit** p50 21.8 ms; **search-miss** p50 15.5 ms / 30 bytes (mixed entropy; still the trigram index — slower than the 6 ms repetitive 21k-block run).
- **Public snapshot** still 2.1 MB (unchanged; not a list).
- Closed cells: **0 failures**.
- **Steady 15 rps / 45 s:** issued = completed = 676, achieved 15.02 rps, p95 total 30.5 ms (coordinated omission), 0 failures.
- **Spike 60 rps / 15 s:** 901 / 901, achieved 59.99 rps, p95 total 28.7 ms, 0 failures.
- **Adverse (tests):** revoked membership, deleted/private journeys and outsider reads return the same 404 as a missing ID (`ScaleAdverseTests`, `ScaleBoundaryTests`). Processed-key union plus crash reclaim is covered. The bench `outsider-404` cell that hit `/conversations/scale-long` is discarded (Sam is a Cloudplay editor → 200); deleted-publication 200 → 404 is valid.
- **Browser (IDE Chromium, warm SPA):** desktop explorer 299 ms / canvas 352 ms to usable; 390×844 explorer and canvas rendered (12 nodes). Playwright chromium was not available.

Do not treat 15 rps local loopback as an Azure B1 rating.

### Phase 5 pin — October 10, 2026

Configuration, fixture, local limits and rollback triggers: `docs/performance/release-envelope.md`. Live canary was not executed.

## Findings across the product

1. **Conversation and authorization reads were coupled — fixed.** `Access.RequireConversation` checks permission from metadata; `Access.ConversationPage` / `MessagePage` load untracked, ordered content with split queries and never select `RawPayload`. `GET /api/conversations/{id}` returns a first page (default 200, max 500, 512 KiB of block text) plus a cursor; remaining pages come from `/messages`. Outsiders get the same 404 as a missing ID; members without the permission still get 403.
2. **List responses grow with the entire project — fixed.** Library, journeys and comparisons return `{items, nextCursor}` with a keyset on `(CreatedAt, Id)` (default 50, max 200). Journeys carry a derived `Preview`/`NodeCount` instead of `Graph`; comparisons carry `id`/`title`/`createdAt` and load the rest from `GET /comparisons/{id}`. Annotations take at most 1,000 rows and accept `nodeId`; publication history takes at most 200. Workspace counts come from `GET /w/{wid}/summary`.
3. **Publication preparation reads all workspace conversations — fixed.** Journey preview/publish load only conversations referenced by the graph and the selected messages; conversation publish does the same. Large publications as queued snapshot jobs remain later work if a snapshot itself becomes too large.
4. **MCP output bounds do not bound database work — query fan-out is fixed.** `Journey(id)` is a single authorized lookup. `FindNode`, `Decisions` and `Related` still parse every granted graph in memory, but they resolve every linked conversation ID in one query. Evidence still fetches at most 101 messages in SQL. Remaining: token budgets and a SQL projection for search/decisions if a grant list grows large.
5. **Journey saves are safe; validation fan-out is fixed.** `Journey.Version` is configured as a concurrency token (`Journey.Data/Features/Journeys/Configuration.cs`), so simultaneous saves produce one success and one 409; a phase 1 test now proves it. (The first revision of this review wrongly said otherwise.) Node validation used to run one query per node and now runs one query per save. Normalizing nodes, edges and steps into tables is a later option if measured edit contention or graph size requires it, not a launch prerequisite.
6. **Search has no dedicated retrieval model — indexed, same semantics.** Library search still uses `Contains` (substring). PostgreSQL now has `pg_trgm` GIN indexes on block text and comment bodies (`SearchTrigramIndexes`); SQLite stays unindexed for local development. Azure Flexible Server must allow-list `PG_TRGM` (`azure.extensions`). A separate search service remains later work if substring matching is not enough.
7. **Import and context work can compete with web reads — lease and bookkeeping are fixed.** Import batches have `LeaseOwner`/`LeaseExpiresAt`; a worker claims with a conditional update, renews while processing, and another instance resumes after expiry. Processed keys live in `ImportProcessedKey` (legacy JSON is unioned in). Parsing can still materialize a whole structured source; CPU isolation is an option B concern.
8. **Raw payloads and snapshots duplicate large text.** Source blobs coexist with raw per-conversation strings and normalized text. Keep original evidence in Blob with hashes and durable references; avoid selecting raw payload columns on read paths. Preserve provenance and unknown fields during migration.
9. **Frontend work was measured on the local SPA, not on Azure.** After login, hash navigation to explorer took 299 ms to usable content (one 57 ms long task) and canvas 352 ms (12 nodes). Dashboard accumulated 139 ms of long tasks. Heap from `performance.memory` in the IDE browser is understated. Virtualize the explorer message list if a single conversation is huge.
10. **Public snapshots — fixed in phase 1.** `GET /api/public/{slug}` returns `Cache-Control: public, no-cache` with an ETag of the publication ID. Revalidation costs one ID lookup and returns 304; deleting a publication still takes effect immediately. A long-lived `max-age` was rejected because withdrawn snapshots would stay cached. The anonymous public routes (snapshot, public list, discussion read) share a per-IP limit of 120 requests/minute, and the discussion read no longer loads the snapshot column.
11. **Startup migrations are serialized, not racy.** Npgsql's `Migrate()` takes `LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE`, so concurrent instances wait rather than collide. Startup still defaults to `Database:MigrateOnStartup=true`; set it to `false` to apply the CI-generated idempotent script (`artifacts/migrations/postgresql.sql`) out of band. Schema changes still need expand/contract while old and new instances overlap. Combined with SignalR without a backplane, the app remains single-instance until option B.
12. **The deployed footprint is now defined, still small.** `infra/main.bicep` provisions App Service S1 (one instance), PostgreSQL Flexible Server 18 (`Standard_B2s`, 32 GB, `PG_TRGM` allowed), and workspace-based Application Insights on a 30-day Log Analytics workspace. Warning emails go to `felipe.ramos@fitcode.io` when App Service plan CPU or memory, or PostgreSQL CPU, memory, or storage, averages above 80% for 15 minutes. The connection string is written to Key Vault as `ConnectionStrings--Journey`. A route allocating 26 MiB per request still matters far more on 1.75 GB than on the benchmark desktop.

## Architecture options

### A. Bounded relational application

PostgreSQL remains the authoritative source for messages, metadata and normalized graph entities. Blob stores source files, attachments and large raw payloads. Add indexed queries, summaries, pagination, streaming and resource limits.

Advantages: smallest migration, transactional consistency, simple operational model, strong fit for structured collaboration. Relative infrastructure cost is lowest. Risks: complex search and context assembly still compete with transactional queries; background work still needs isolation. This is the mandatory foundation for every other option, not a reason to keep the current single-process worker design.

**This is the recommended launch topology**, together with a database-lease job queue so imports are claimed safely. Every problem the baseline measured is fixed by bounding queries and responses; none of them requires projections, a message bus or a second store. A database can hold substantial text; bounded access patterns matter more than moving all text out. Launch A only after it passes the agreed workload.

### B. Hybrid read architecture — the planned next step, triggered by evidence

Build A, then separate durable background work from the web tier. Maintain versioned read projections for journey summaries, timelines, comparison summaries, and publication manifests. Store immutable content chunks/snapshots in Blob when they are large. Cache selected hot, versioned projections. Add an indexed context-retrieval path rather than traversing every project entity per request.

Azure mapping: App Service for the API/frontend; PostgreSQL Flexible Server as the authority; Blob for files/chunks/snapshots; Service Bus or a proven database lease queue for durable jobs; separately deployed workers. Add Azure Managed Redis only where measured cache hit rates justify it. PostgreSQL full-text search can be the first search implementation. Azure AI Search is an option for independently scaled hybrid retrieval, not an automatic prerequisite.

Advantages: isolates import/index/context CPU, bounds read amplification, supports horizontal web scaling after shared state is addressed. Relative cost/engineering effort is moderate. Risks: eventual projections, invalidation, index lag and cross-store recovery. Use transactional outbox events, idempotent consumers, content version identifiers, explicit freshness states, and authoritative permission checks.

**Move to B when any of these triggers fires:** a second web instance is needed for availability or load; imports or indexing measurably degrade read latency at the agreed workload; or the context-retrieval path needs precomputed embeddings or summaries. Add each cache/search service only when a benchmark demonstrates its purpose. Two web instances improve availability only after worker claiming, startup migrations, SignalR, caches and revocation behavior are safe across instances.

### C. Object-first content with a database catalog

PostgreSQL stores ownership, relationships, chunk manifests and indexes; most message/document bodies reside in immutable Blob chunks. A search index supplies candidate IDs/snippets, then the system fetches only selected chunks. Bundle sensible chunks rather than one Blob per small message or one enormous Blob per project.

Advantages: suitable when immutable content volume dominates database cost and reads are selective. Risks: many Blob operations, retrieval latency, more joins across services, index lag, and difficult consistent backup/deletion. Annotation anchors and stable message IDs must survive chunk rewrites. Database savings do not guarantee lower total cost.

Choose C if realistic benchmarks show large content bodies—not query shape—dominate PostgreSQL I/O/storage, and context requests usually touch a small fraction of a corpus. Avoid an immediate migration to this merely because the product stores text.

### D. Separate read store / replicas / broader CQRS

Use an independently scaled search/read store or read replicas for stale-tolerant workloads, while primary PostgreSQL owns transactions and permission changes. Keep fresh writes and access-revocation decisions on the authoritative path.

Advantages: separates heavy read demand and specialized query shapes. Relative infrastructure and operational cost is higher. Risks: replica lag, stale permissions, routing, failover complexity and extra consistency work. A replica multiplies capacity only for eligible reads; it does not fix unbounded responses or inefficient queries.

Choose D when optimized bounded reads saturate the primary under the agreed workload, or measurements show a distinct search/reporting load that deserves isolation. Microservices, sharding and a graph database are not justified by the current evidence. Revisit tenant partitioning when measured tenant skew and index size warrant it.

## Large-context tasks need their own data path

Model a context request as a bounded retrieval job, not “load the entire project.” The reviewed code does not yet provide a general token-budgeted context-pack service; MCP's clipping is not an equivalent design.

1. Resolve tenant, workspace, allowed objects and the required data revision using small indexed queries.
2. Search authorized candidates across messages, documents, decisions, annotations, journey nodes and comparisons. Apply an ACL filter before retrieval and recheck current permission before returning material; a stale search index or cache must never grant access.
3. Retrieve only selected chunks, with bounded parallelism and limits on candidate count, bytes, tokens, query count and time. Preserve source/message/block IDs, hashes, citations and revision identifiers.
4. Deduplicate repeated evidence and select within a token budget; do not rely only on character truncation. Report omitted material and offer follow-up cursors or an asynchronous full export.
5. Cache immutable chunk data by content hash. Cache a context manifest by tenant, permission version, query, source revisions, retrieval configuration and tokenizer/model settings. Recheck access even on a cache hit. Summaries must be versioned, traceable, and invalidated when evidence changes.
6. Precompute embeddings/summaries/index projections in workers with idempotent jobs. Separate retrieval latency, queue latency, time to first model token, and total inference duration in telemetry.

The model's context limit, language/tokenization, inference throughput and provider limits remain separate capacity constraints. A larger database or context window cannot substitute for retrieval quality testing.

## Proposed launch contract — targets, not measured promises

These are provisional targets pending product workload and budget decisions:

- **Pilot corpus:** 100,000 messages, 10,000 document chunks, 100 journeys with up to 1,000 nodes each; uneven workspace sizes.
- **Launch corpus:** 1 million messages, 100,000 document chunks, 1,000 journeys; include one hot tenant and a 10,000-node journey to test rejection/progressive loading.
- **Stress corpus:** 10x the launch corpus, deeper threads, large metadata/tool results, realistic text entropy, many small files and some large files. Test both total tenant volume and a single oversized object.
- **Read traffic:** arrival-rate runs at 10, 25, 50 and 100 requests/second, with burst tests and at least 50 active sessions. Concurrent users are not interchangeable with requests/second. Proposed mix: 30% list/summary, 25% message pages, 15% search, 10% journey windows, 10% comparisons/annotations, 5% public snapshots, 5% context retrieval.
- **Context jobs:** 1, 5 and 10 concurrent tasks selecting 32k/128k/512k-token budgets where the intended model supports them. Measure retrieval separately from generation. Include 0%, 50% and 90% content-cache hit scenarios.
- **Background work:** 0, 2 and 5 simultaneous imports/indexing jobs with realistic file distributions while read load continues. Include cancellation, process restart and retry.
- **Initial objectives:** list/message-page p95 under 300 ms and p99 under 1 s; search p95 under 750 ms; initial journey view p95 under 500 ms; context-manifest first response under 1 s, and 128k-token retrieval p95 under 3 s excluding inference. These apply within the chosen Azure region and must include a separate browser/network budget.
- **Payload budgets:** summaries at most 100 KiB; message pages at most 512 KiB; context/export downloads explicitly streamed or asynchronous. Server-side caps apply even if a client omits pagination.
- **Reliability:** under 0.1% unexpected errors at target steady load; zero cross-tenant disclosure; no lost committed jobs or duplicate externally visible side effects; deliberate overload returns bounded 429/503 responses with retry guidance.
- **Headroom:** proposed steady-state CPU below 65%, memory below 70%, bounded connection pools and queue age, no monotonic memory growth. Determine actual safe thresholds by saturation testing, not by these percentages alone.

Do not multiply today's local throughput by an Azure instance count. Capacity depends on the working set, hardware, network, storage latency, cache hit rate, request mix, database plans and contention.

## Execution plan

Sizes are relative (S: a few days, M: about a week, L: two weeks or more for one developer). Phases 0–3 are complete. Phase 4 local substitute and phase 5 pin are complete; Azure soak/canary remain.

### Phase 0 — Make the benchmark trustworthy (S) — done October 9, 2026

Runner rebuilt (Kestrel loopback, streaming client, server garbage collection, 100 samples, concurrency 1/2/4/8, CPU/GC/query metrics, `ANALYZE`, `EXPLAIN` of the slowest query per route). Explanations for the flat throughput and the search growth are in "Phase 0" above. Allocation totals remain process-wide; they occasionally read negative under server garbage collection and should be treated as approximate.

### Phase 1 — Security and correctness fixes (M) — done October 9, 2026

- **Authorization before content:** `Access.RequireConversation` (metadata only) and an untracked, split-query `Access.Conversation` that never selects `RawPayload`, used across the callers listed in finding 1.
- **Conversation response:** explicit shape without `rawPayload`. Provenance and diagnostics stay because the client reads provenance.
- **Journey concurrency:** already a concurrency token; now covered by a test. Node validation is a single query.
- **Comparison validation:** metadata workspace checks and one query per side for evidence links.
- **Public snapshots:** revalidating cache headers with an ETag, and a per-IP rate limit on anonymous public routes.

Tests in `tests/ScaleBoundaryTests.cs` prove an outsider read is indistinguishable from a missing ID and issues no message query, the response has no `rawPayload` and keeps message order, project assignment persists without loading content, concurrent journey saves produce exactly one 409, and public snapshots revalidate with 304 and return 404 after deletion.

### Phase 2 — Bound every read path (L) — done October 9, 2026

- **Library:** keyset pagination on `(CreatedAt, Id)` (default 50, max 200); `GET /w/{wid}/summary` for counts and providers; client search and "Load more" use the envelope.
- **Lists:** journey summaries carry `Preview`/`NodeCount` (derived on save, lazily backfilled); `GET /w/{wid}/journey-options` and `GET /conversations/{id}/journeys` for pickers and Explorer; comparisons are summaries plus `GET /comparisons/{id}`. Annotations take 1,000 and accept `nodeId`; publication history takes 200.
- **Messages:** `GET /conversations/{id}` returns the first page; `/messages` continues. Explorer, Comparison and Publish call `loadConversation`; Canvas requests `limit=3`.
- **Publishing:** `Publishing.Sources` loads referenced conversations and selected messages only.
- **MCP:** `Journey(id)` is one query; `SafeNodes` batches conversation checks.
- **Search:** uncorrelated ID sets plus PostgreSQL `pg_trgm` GIN indexes; substring semantics unchanged.
- **Regression guard:** `tests/ScalePagingTests.cs` asserts query count stays constant as the workspace grows, pages have no gaps, and an import lease has one owner.

**Exit:** rerun the benchmark; every list is at most 100 KiB, a message page at most 512 KiB, and no route's work grows with workspace size.

### Phase 3 — Safe background work and deployment (M) — done October 9, 2026

- **Import lease.** `LeaseOwner`/`LeaseExpiresAt` with a conditional claim, renewal, and reclaim after expiry (`ImportLease`).
- **Import bookkeeping.** `ImportProcessedKey` table; legacy `ProcessedKeys` JSON is unioned in and no longer rewritten.
- **Migrations.** Startup still migrates by default (`Database:MigrateOnStartup`). Set it to `false` to apply the CI-generated idempotent PostgreSQL script (`artifacts/migrations/postgresql.sql`) out of band. Concurrent `Migrate()` is serialized by Npgsql's table lock; GitHub runners never receive a database connection.
- **Infrastructure.** `infra/main.bicep` now provisions PostgreSQL Flexible Server 18 (`Standard_B2s`, 32 GB, `azure.extensions=PG_TRGM`) and writes `ConnectionStrings--Journey` to Key Vault.

**Exit:** two workers against one database never process the same batch (proven); killing the process mid-import resumes from the processed-key table; operators can apply the script before a version that sets `MigrateOnStartup=false`.

### Phase 4 — Production-equivalent load (L) — local substitute October 10, 2026; Azure not run

- Realistic corpus generator: ScaleBench `pilot` profile (mixed-entropy `Phrase`, zipf conversation sizes, 4–64 KiB raw, 256 KiB long raw, 1,000-node `scale-j-large`, 40 personal conversations, 2 MiB public snapshot).
- Local gate 3/4 substitute: closed-loop plus 15 rps / 45 s arrival and 60 rps / 15 s spike; adverse unit tests for revoke, delete, private, lease crash.
- Browser: time to usable content, long tasks and heap on dashboard, library, explorer and canvas (desktop + 390×844). Playwright spec is `src/Journey.Client/e2e/scale-browser.spec.ts`.

**Exit (local):** lists stay bounded, arrival/spike completed with zero failures, outsider/revoked/deleted look like missing. **Not exited:** Azure 30-minute / 2-hour soak, Blob, failover, rolling deploy.

### Phase 5 — Release (S) — pin written October 10, 2026; canary not run

Gate 5: `docs/performance/release-envelope.md` pins the working tree, fixture, intended SKUs, published caps and rollback triggers. Live canary was not executed.

### Triggered work (not scheduled)

- **Option B** when a trigger in the architecture section fires: separately deployed workers, outbox events, versioned read projections, and Azure SignalR Service (or a Redis backplane) before running a second instance.
- **Context-retrieval service** when large-context tasks enter scope (see the section above).
- **Graph normalization** if measured edit contention or graph size requires it.
- **Options C and D** only on their measured triggers.

## Release gates

**Gate 1 — correctness and bounds.** Phases 0–2 complete. Capture SQL plans with `EXPLAIN (ANALYZE, BUFFERS)`, rows/bytes read, query counts and allocation profiles, and keep the regression assertions that work per request stays bounded as the corpus grows.

**Gate 2 — safe background work.** Phase 3 complete: durable claims, retries, cancellation, idempotency, bounded worker concurrency and memory. Test simultaneous edits and worker crash recovery before enabling more than one web instance.

**Gate 3 — production-equivalent load.** Dedicated Azure test environment with the selected App Service tier/count, PostgreSQL tier/storage, Blob networking and actual cache/search choices. Run 30-minute steady loads, 5-minute spikes, and a 2-hour soak. Use an arrival-rate tool such as k6 or Azure Load Testing; account for coordinated omission and compare cache-cold versus warm behavior. Do not load-test the live customer database.

**Gate 4 — adverse conditions.** Slow Blob reads, unavailable cache, database failover, worker restarts, backlog recovery, permission revocation, deleted documents, index lag and rolling deployment. Test on mobile/low-memory browsers; measure time to usable content, heap, long tasks and canvas interactions.

**Gate 5 — release decision.** Pin build commit, fixture version, configuration, instance/database sizes and results; review latency/error/quality targets and recovery drills. Publish tested quotas and concurrency limits. Canary rollout with dashboards, budgets and rollback triggers. Defer broad availability if the envelope fails; increase hardware only when evidence shows resource saturation after query fixes.

## Run the local baseline again

A disposable container keeps the benchmark away from every app database. From the repository root:

```powershell
$pw = [guid]::NewGuid().ToString("N")
docker run -d --name journey-scalebench-pg -e POSTGRES_PASSWORD=$pw -p 5434:5432 postgres:17
$env:JOURNEY_BENCH_POSTGRES = "Host=localhost;Port=5434;Username=postgres;Password=$pw"
dotnet run --project tools/Journey.ScaleBench -c Release -- artifacts/scale-baseline.json
docker rm -f journey-scalebench-pg
```

`JOURNEY_BENCH_SAMPLES` overrides the default of 100 samples per cell (use 3 for a smoke run). `JOURNEY_BENCH_PROFILE=pilot` switches to the phase 4 corpus, closed-plus-arrival scenarios, and `JOURNEY_BENCH_RPS` / `JOURNEY_BENCH_DURATION` for the open-loop cell. The runner creates a uniquely named `journey_scale_<guid>` database, applies the server's migrations and demo seed, and writes the results plus a `.plans.txt` file with `EXPLAIN (ANALYZE, BUFFERS)` of the slowest query per route. It serves the real app over loopback HTTP with Kestrel, but without TLS, compression or a network hop, so it cannot certify Azure performance. Compare runs on the same machine and database only.

## Decisions needed

Agree on the largest supported project and individual object, concurrent read/context demand, launch latency targets, allowed freshness lag, deployment budget, App Service and PostgreSQL tiers, and rollout scope. The local pin assumes B1 + PostgreSQL `Standard_B1ms` and the published page caps; Azure soak still needs those answers confirmed. B, C and D require measured triggers rather than an upfront commitment.

## Primary guidance

- [EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying): projections, indexes, pagination and bounded result sets.
- [EF single/split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries): duplicated wide columns and related-data loading tradeoffs.
- [Azure web–queue–worker](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/web-queue-worker): independently deployed web and worker responsibilities.
- [Azure caching guidance](https://learn.microsoft.com/en-us/azure/architecture/best-practices/caching): cache choice and consistency considerations.
- [Azure PostgreSQL read replicas](https://learn.microsoft.com/en-us/azure/postgresql/read-replica/concepts-read-replicas): asynchronous replication and lag considerations.
- [Azure AI Search access controls](https://learn.microsoft.com/en-us/azure/search/search-document-level-access-overview): indexed retrieval must preserve document authorization.
