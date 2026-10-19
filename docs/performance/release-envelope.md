# Release envelope — Phase 5 pin

Pinned: 10 October 2026. This is a **local pilot pin**, not an Azure capacity certificate.

## What was tested

| Item | Value |
| --- | --- |
| Last committed SHA | `3882e9f83ee663b4d4fb20c2869dc864ad6f3412` (`Performance Improvement`) |
| Code under test | Uncommitted working tree on `main` as of 10 October 2026 (bounded reads, import lease, trigram indexes, ScaleBench `pilot` profile, adverse tests) |
| Fixture | `JOURNEY_BENCH_PROFILE=pilot` — 1,541 conversations, 25,390 messages, 16 journeys (15 small + `scale-j-large` 1,000 nodes), 150 comparisons, zipf sizes, mixed-entropy text, 40 `personal` conversations, 2 MiB public snapshot |
| Bench host | Desktop loopback Kestrel, .NET 10.0.12, server GC, 16 cores, disposable PostgreSQL 17 on port 5434 |
| Intended Azure SKU (not exercised) | App Service S1, 1 instance; PostgreSQL Flexible Server 18 `Standard_B2s`, 32 GB, `azure.extensions=PG_TRGM` |
| Browser | Cursor IDE Chromium on `http://127.0.0.1:5080` after Alex login; desktop 1920×1080 and emulated 390×844 |

Raw numbers: `docs/performance/scale-phase4-pilot-2026-10-10.json`, `scale-phase4-browser-desktop-2026-10-10.json`, `scale-phase4-browser-mobile-2026-10-10.json`.

## Local results that held

- Closed-loop cells: **0 failures**. Lists stay small: library 14.5 KiB, journey list 64 KiB, comparisons 4.7 KiB, workspace summary 185 B. Long conversation page 120 KiB. 1,000-node journey GET 266 KiB, p50 4.4 ms (c=1) / 11 ms (c=8).
- Search-miss p50 **15.5 ms** / 30 B (mixed-entropy corpus; still index-backed). Search-hit p50 21.8 ms.
- Steady arrival **15 rps / 45 s**: issued = completed = 676, achieved 15.02 rps, p95 total 30.5 ms, 0 failures.
- Spike **60 rps / 15 s**: 901 / 901, achieved 59.99 rps, p95 total 28.7 ms, 0 failures.
- Desktop time-to-usable after login (warm SPA hash change): dashboard 22 ms, library 33 ms, explorer 299 ms (1 long task, 57 ms), canvas 352 ms (12 nodes), compare 328 ms. Dashboard accumulated 139 ms of long tasks.
- Mobile 390×844: explorer and canvas rendered; hash-change times were warm (≤36 ms). Canvas still 12 nodes.

## Published limits (enforced in code)

| Limit | Value |
| --- | --- |
| List page | default 50, max 200 |
| Conversation messages | first page default 200, max 500, 512 KiB of block text |
| Journey list preview | 16 nodes, 32 edges (`GraphPreview`) |
| Annotations | 1,000; publication history 200 |
| Anonymous public routes | 120 requests / minute / IP |
| Topology | **one** App Service instance until SignalR has a backplane and import workers are isolated (option B) |

These are product caps, not proven Azure throughput.

## Not executed — do not infer from loopback

- Dedicated Azure test environment, 30-minute steady load, 5-minute spike, 2-hour soak
- WAN / TLS / compression, App Service ingress, Blob latency, Key Vault
- Database failover, rolling deploy, cache-unavailable, slow Blob
- Playwright chromium e2e (`scale-browser.spec.ts` exists; runner cache had no browser)
- Live canary or production traffic

The bench `adverse` cell that requested `/conversations/scale-long` as an outsider is **not** evidence: Sam is a Cloudplay editor, so that URL returned 200. Outsider / revoked / deleted / private 404s are proven by `ScaleBoundaryTests` and `ScaleAdverseTests`. Deleted publication 200 → 404 in the bench is valid.

## Canary and rollback (playbook only)

When Azure is available, canary on the pinned SKU with the same fixture shape (or a subset) and these **rollback triggers**:

1. List or message-page p95 above 300 ms, or search p95 above 750 ms, for five consecutive minutes.
2. Unexpected 5xx / 429 (excluding deliberate overload tests) above 0.1% of requests.
3. Any outsider or revoked member receiving 200 on a hidden conversation or private journey (must be the same 404 body as a missing ID).
4. Two workers claiming the same import batch, or a crash that does not resume from `ImportProcessedKey`.
5. Monotonic working-set or connection-pool growth across a soak hour.
6. Failed `/api/health` or failed authenticated smoke after deploy.

**Rollback:** redeploy the previous application artifact only if its schema is compatible. Database rollback is a restore, not an old binary. Never assume deploying old bits reverses migrations. Drain imports before restart (`LeaseExpiresAt` lets another worker reclaim). Stay on one instance.

**Defer broad availability** if any trigger fires. Increase hardware only when evidence shows resource saturation after the query is already bounded.

## How to re-run the local pin

```powershell
$pw = [guid]::NewGuid().ToString("N")
docker run -d --name journey-scalebench-pg -e POSTGRES_PASSWORD=$pw -p 5434:5432 postgres:17
$env:JOURNEY_BENCH_POSTGRES = "Host=localhost;Port=5434;Username=postgres;Password=$pw"
$env:JOURNEY_BENCH_PROFILE = "pilot"
$env:JOURNEY_BENCH_SAMPLES = "15"
$env:JOURNEY_BENCH_RPS = "15"
$env:JOURNEY_BENCH_DURATION = "45"
dotnet run --project tools/Journey.ScaleBench -c Release -- docs/performance/scale-phase4-pilot.json
docker rm -f journey-scalebench-pg
```
