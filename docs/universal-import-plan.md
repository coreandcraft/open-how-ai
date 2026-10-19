# Universal import engine integration plan

## Reuse

Keep the current Domain/Data/Service/Server/Client split. Reuse Access for every workspace operation, ImportSource for unchanged bytes, ImportBatch and the hosted worker for background persistence, and the existing preview/selection wizard. Source content remains separate from annotations, journeys and publications.

## Changes

1. Add a DI-discovered capability registry with content-based ranked detection and explicit adapter selection. Keep existing importer methods compatible.
2. Add source identity, source grouping, project associations, provenance, original parent IDs, import conflicts and session relationships. Application projects are independent of provider groups.
3. Add version-limited native session adapters, archive inspection and explicit generic mappings. Preserve unknown events in raw records and diagnostic metadata.
4. Persist stable external-ID imports incrementally. Append new messages; unchanged messages retain IDs/blocks; changed messages become reviewable conflicts rather than silently modifying annotated text.
5. Extend preview with provider/group trees, diagnostics, candidate formats and destination project selection. Expose imported source relationships in the explorer.
6. Add representative fixtures, boundary tests and a compatibility matrix with implemented/experimental/pending states.

## Unknowns and boundaries

Cursor storage is not a stable public contract; parsers must identify specific table/key/payload shapes and reject unsupported versions. Browser uploads cannot access omitted SQLite WAL files; require a consistent supplied database, operate on a read-only temporary copy, and never query unrelated rows. Missing project/parent/attachment targets remain unresolved. No automatic scanning of installed tools or user home directories. Manual upload must work without it. Large-history limits must be explicit; current SQLite prototype is bounded and is not validated for millions of messages. Raw source retention must not delete bytes silently. No export features are added.

## Follow-up implementation decisions

- Changed source records remain pending until an editor explicitly accepts them. Retained message versions include the old source and anchor state; quote recovery is conservative and orphaning is visible. Public snapshots remain immutable.
- Supplied-file linkage uses exact paths only. Available files are privately downloadable and do not enter public representations. Missing or ambiguous references stay unresolved.
- SourceWorkspace and AgentExecution store explicit source paths and call/result IDs separately from curator journey edges. External project metadata reconciles by provider, source identity, and exact project ID.
- Optional discovery has an operator user allowlist, loopback-only routes, per-scan consent, relative exclusions, expiring file selections, and changed-file checks. No discovery runs on startup.
- Originals spool into private files, optionally using authenticated chunk encryption. Source-file retention defaults to indefinite and only expires successful raw transcript/container files; it does not erase normalized history or attachments.
- Supported top-level account arrays stream by conversation, preview excerpts are materialized with strict bounds, and EF tracking is released after each record. Native single-session parsers remain bounded by their per-source materialization; no million-message benchmark claim is made.
