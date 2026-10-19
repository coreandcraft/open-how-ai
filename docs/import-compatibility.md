# Import compatibility and extension guide

This document describes **import**, not export. All parsers consume user-supplied files. Optional local discovery is implemented behind explicit consent, loopback access, and an operator-controlled user allowlist. Manual import works independently.

## Implemented and fixture-tested variants

- ChatGPT: JSON objects/arrays containing message `mapping`; split account conversation files inside supplied archives are parsed separately. Original project IDs are retained when present. Unknown account metadata files produce diagnostics. `library_files.json` maps each `file_id` to a supplied `file_id.dat` payload. Mapped payloads are stored under that id, using the declared extension and original file name, including documents, archives, and other supplied types.
- Claude: JSON `chat_messages` records, UUIDs, explicit parents, content blocks and project UUIDs. `projects.json` with UUID/name/prompt_template is an **experimental** metadata-only variant; projects can exist without conversations.
- Claude Code: session JSONL with sessionId, UUID, parentUuid and message content. Agent IDs create distinct child-session records. Summary/compaction records remain events. Malformed lines produce diagnostics and remain in the original source.
- Codex: rollout JSONL with session_meta and payload records. Messages, tool calls/results, compacted and unknown events remain available. Fork and thread_spawn parent IDs are resolved only by exact identity. Event mirrors are retained as events rather than merged with source messages.
- UTF-8 JSONL byte-order marks are accepted. Zstandard-compressed `.jsonl.zst` and `.ndjson.zst` transcripts are decoded for direct uploads and entries inside ZIP/TAR.GZ files. Compressed and expanded bytes count toward the import quota. Unsanitized imports retain the compressed original plus the decoded transcript; sanitized imports retain only the sanitized transcript.
- Codex desktop `codex-dev.db` is a sidebar/catalog database, not a supported conversation export. The importer identifies its catalog schema and explains that titles/IDs cannot replace message bodies. For locally stored Codex chats, supply the rollout JSONL files under `.codex/sessions`; for ChatGPT, supply a conversation export containing messages. SQLite `-wal`/`-shm` sidecars are identified separately and are not replayed. A catalog plus sidecars does not provide a transcript when message bodies are absent.
- Gemini CLI: JSON ConversationRecord with sessionId/projectHash/messages. This does not cover all checkpoint or newer JSONL patch formats.
- API messages: OpenAI choices/message JSON and Anthropic type=message content blocks; generic role/content arrays support compatible request messages.
- Generic: structured conversation JSON, role/content arrays, JSONL/NDJSON, Markdown and plain text. Text documents are not split into guessed turns.
- LangChain: serialized type/data.content message arrays.
- HTML: document text with scripts/styles removed from the rendered representation; original bytes retained privately. No role reconstruction or execution.
- Explicit mapping: JSON dotted field paths, JSONL records, CSV columns and a supplied SQLite table. Fields include role, content, ID, parent, conversation, project and timestamp. No arbitrary SQL is accepted.
- Archives: ZIP and TAR.GZ, content-signature detection, no extraction to filesystem, traversal/symlink/nested-archive rejection, bounded reads.

## Experimental, fixture-tested narrow variants

- Cursor: ItemTable composer.composerData with allComposers/fullConversation; cursorDiskKV composerData keys with inline conversation arrays. No universal support for separately stored bubbles, encrypted records, CLI streams or future layouts. A consistent SQLite file is required; omitted WAL files cannot be reconstructed.
- VS Code/Copilot: requests/message.text/response records.
- Continue: history entries containing message.role/content.
- OpenCode: info session metadata plus messages/info/parts records. Separate storage databases/directories need explicit mapping; supplied parentID stays unresolved until its target is imported.
- Cline/Roo Code: api_conversation_history.json role/content arrays. They are deliberately labeled together because this shape does not reliably identify which application created it.
- Aider: .aider textual histories retained as documents; no guessed execution boundaries.
- OpenTelemetry: OTLP JSON resourceSpans/scopeSpans/spans; trace and parent-span IDs are retained, spans rendered as events. Arbitrary agent trace dialects need mapping.

These statuses apply to the fixture shape, not to every version of the provider. Synthetic fixtures demonstrate parser behavior, not universal compatibility with undocumented schemas.

## Pending sample validation

Windsurf native histories, Cursor CLI and unsupported Cursor storage versions, Gemini consumer exports/newer CLI patches, Grok, Perplexity, Poe, Microsoft Copilot, Google AI Studio, LM Studio, Jan, Open WebUI, LibreChat, AnythingLLM, and unspecified OCF dialects. Use generic compatible data or manual mapping where applicable. Do not relabel unknown payloads as a confirmed native format.

## Integration and data semantics

See universal-import-plan.md for the pre-implementation plan. Domain keeps source grouping separate from ApplicationProject. Conversation.ApplicationProjectId is user curation; ExternalProjectId, SourcePath, SourceIdentity and ParentConversationExternalId preserve source facts. SourceProject preserves project metadata; SourceRelationship tracks confirmed, unresolved or user-associated session links. A source graph projection exposes recorded message parents, tool IDs and attachment references; these are not curator journey edges.

Every preview records the selected adapter/version, source path and SHA-256 hash. Unknown fields remain in raw payloads/metadata. Source timestamp strings, including time zones, remain in raw records; normalized timestamps are UTC. Missing chronology is displayed in source-record order, not fabricated wall-clock order.

Imports are private. Workspace permissions govern all reads/writes. Content is never executed and no imported content is published automatically. Original bytes are preserved in private file storage (legacy SQLite sources remain readable). Optional authenticated encryption protects newly stored original files. Annotation artifacts use a separate store.

## Incremental imports and conflict review

Use the same source account/installation label for updates, different labels for unrelated accounts. Matching uses workspace + provider + source label + external conversation ID. Exact source-content fingerprints are skipped. New external message IDs append to an existing conversation; existing message/block IDs remain stable. Changed message contents produce ImportConflict records while the old annotated content stays intact. The source inspector displays incoming changes and can mark the original as retained. The source inspector can explicitly accept an incoming revision. The previous message and anchor positions are retained in ImportedMessageVersion. Stable message/block slots remain usable; unique quote/context matches migrate annotations, while missing or ambiguous quotes become orphaned. Stale conflicts are rejected. Previous source versions are available in the private explorer. Manual project assignments are never replaced by reimport.

Per-source persisted checkpoints make completed records resumable without replaying counters. Jobs support cancel and retry endpoints; the UI offers cancellation. Failures in one file do not discard successful files. Original imported IDs without verified external identity cannot reliably support incremental changes; they remain distinct source versions or unchanged-content matches.

## Explicit prototype limits

15 GB total upload including archive/container bytes by default; 20 GB for operator-enabled user IDs; 500 uploaded files; 50,000 entries per archive; mapped SQLite at most 100,000 rows. Account conversation arrays stream by record; other parsers still materialize individual source files. Millions of messages have not been load-tested end to end. SQLite import uses a read-only temporary copy of the supplied bytes and fixed/validated queries. Consent-based local discovery, exact supplied-file matching, source-file encryption, and source-file retention are now implemented (configuration below). Live database backup, undocumented native schemas, similarity-based association, and guessed project reconstruction are not implemented.

Tool details and unknown event payloads remain available in normalized blocks/raw records. AgentExecution pairs explicit call/result IDs. SourceWorkspace records verified source paths; exact provider/source/project IDs reconcile separately supplied Claude project metadata. Unrecorded repository associations and undocumented account metadata are left unresolved. The compatibility limits are shown rather than silently flattening or guessing data.

## Add an adapter

Implement IConversationImporter in Journey.Service/Features/Imports, declare a unique Capability.Id and version/status, content-based CanImport and confidence, and PreviewAsync/ImportAsync. Inherit Importer for normalization/provenance or SessionImporter for JSONL and explicit parent resolution. Register it as IConversationImporter in DependencyInjection; no worker/provider switch is needed. Preserve SourceInput.SourceIdentity. Return warnings for unsupported content, never fabricate hidden reasoning or relationships. Add a sanitized fixture, detection/graph/fidelity regression tests, and a compatibility entry before describing support.

Tests generate Cursor SQLite input in an isolated temporary database, never from a developer's installed application. UniversalImportFlowTests exercises preview/commit/background import, cross-provider grouping, exact reimport and unresolved parents. UniversalImportTests covers fixtures, partial JSONL recovery, mapping, graph preservation and annotation-safe increments.

Primary schema references consulted:
- https://github.com/openai/codex/blob/main/codex-rs/protocol/src/protocol.rs
- https://github.com/openai/codex/blob/main/codex-rs/rollout/src/lib.rs
- https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/services/chatRecordingTypes.ts
- https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/services/chatRecordingService.ts


## Recovery and private files

The preview lists signature-recognized supplied PNG/JPEG/GIF/WebP, WAV/ID3/Ogg, and PDF files. Authors can turn private file linking off before committing. References match an exact upload path or an exact path relative to the transcript; duplicate matches remain unresolved. A ChatGPT library id in `file-service://` or `sediment://` matches the supplied payload when that id is an unambiguous path segment. Remote URLs are never fetched. Source evidence shows missing files and unmatched tool results. Downloads require conversation/workspace access. Images, PDFs, video, and audio are shown inline; text and Office Open XML documents can be read as text. Archives, executables, and other files are downloads and are never served as active pages. Imported binaries are excluded from publication snapshots.

Video containers are also recognized from their contents: MP4/M4V, MOV with an `ftyp` box, WebM, Matroska/MKV, and AVI. Folder and archive imports list them under supplied attachments and link exact transcript references using the same private-file flow. Recognition identifies the container, not its codecs or playback validity. Videos remain downloadable originals; inline playback, transcoding, and generated thumbnails are not implemented. Older MOV files without an `ftyp` box are not detected.

## Local discovery and storage configuration

All options are deployment configuration under `Imports` (environment variables use double underscores). Defaults do not scan or expire sources.

- `DiscoveryUserIds`: explicit user IDs allowed to use the local-only discovery screen. The user then chooses Claude Code, Codex, or Gemini CLI transcript roots, excludes relative directories, and grants consent before a read-only listing. File selections expire after ten minutes; modified files require another scan. No credentials or arbitrary application directories are read. Cursor live SQLite discovery is not enabled: manually supply a consistent snapshot.
- `EncryptionKey`: optional base64 32-byte key supplied through deployment secrets. Newly stored files use AES-256-GCM authenticated chunks bound to the source ID and chunk order, with an authenticated final record. Keep this key with protected backups; losing it makes encrypted originals unreadable. Existing plaintext sources are not silently rewritten. Seekable parsing uses an OS temporary file with delete-on-close; protect the host disk and temporary directory. Normalized database content is not encrypted by this option.
- `SourceFileRetentionDays`: positive number expires original non-attachment source files in successfully completed batches; absent or zero retains indefinitely. Hashes, normalized messages, metadata, accepted-version history, comments, and linked assets remain. This is a source-file retention policy, not a whole-account erasure mechanism. Failed/cancelled jobs remain resumable.

## Streaming and regression fixtures

The browser submits previews with `background=true`. After receiving and staging the upload,
the API returns `202 Accepted` with a preview ID; short authenticated polls to
`GET /api/w/{wid}/imports/previews/{id}` report progress and eventually return the usual preview.
Archive inspection, sanitization, source storage, parsing and duplicate checks run in a hosted
worker with a separate service scope and are independent of the upload request's lifetime.
This avoids Azure App Service's approximately 240-second Linux request timeout during processing.
The upload transfer itself must still complete within the gateway's request limits.

Preview staging uses temporary delete-on-close files, including when sanitization is enabled;
only the existing sanitization/consent flow may persist source content. One preview is processed
at a time, with at most two staged/active uploads per app instance; excess requests receive 429.
Preview jobs and results are instance-local. App Service client affinity is enabled in the
deployment template; keep it enabled when scaling to multiple instances. Up to 16 completed
results are cached for two hours. An expired/evicted preview or app restart returns 410 with
instructions to upload again. Committed imports retain their existing durable checkpoints.
Clients omitting `background=true` retain the synchronous preview API for compatibility.

Preview parses seekable local staging/sanitized streams instead of downloading newly stored Blob
sources. Attachment identification reads a bounded signature. JSON/JSONL detectors share one
parsed representation, and preview duplicate/update checks use at most two queries per 500
conversations. Commit recovery runs once after the batch; persisted processed keys identify the
incoming source for existing conversations, including after cancellation/retry. A separate
database context renews the lease and checks cancellation every ten seconds during long reads.

The wizard's **Processing timings** panel shows live and completed stage durations. The same
`fileProgress` in progress, preview and batch responses contains `elapsedMilliseconds`, `timings`
(each stage's `milliseconds`, completed `operations` and `active` operations), and `metrics`
(uploaded/stored/parsed bytes, conversations, duplicate queries and source readbacks). Preview
source readbacks should be zero. Timings use a monotonic clock; operation counts describe timed
calls, not necessarily files. Stages may overlap and should not be added to obtain wall time.
Sanitization includes reading/decompressing the source when enabled. Results in memory have the
same expiry/restart limits as progress; completion logs retain the per-stage totals in Azure.
Logs contain generated run IDs and aggregate timings/counts, never filenames, content or rules.

Application Insights query for stage totals after deployment:

```kusto
traces
| where isnotempty(tostring(customDimensions.ImportStage))
| project timestamp, phase=tostring(customDimensions.ImportPhase),
    run=tostring(customDimensions.ImportId), stage=tostring(customDimensions.ImportStage),
    milliseconds=todouble(customDimensions.DurationMs), operations=toint(customDimensions.Operations)
| order by timestamp desc
```

Run `dotnet run --project tools/Journey.ImportBench -c Release -- 500000000 147` for a synthetic
500 MB, 147-file preview with sanitization and pattern scanning enabled. It creates an isolated
temporary SQLite database and ZIP, deletes them afterward, and writes a unique JSON timing report
under `artifacts`. This measures the actual preview pipeline with local storage; it excludes Azure
network/database latency and commit work. Compare Azure stage logs separately for production.

Supported top-level JSON conversation arrays stream one conversation per parser invocation, retaining the full original file hash and a `$[index]` record location. ZIP/TAR entries spool individually, and EF tracking is released after each persisted conversation. Preview message excerpts are bounded; source content is unchanged. Single-session JSONL, wrapper-object JSON, and individual very large conversations still require per-source/per-conversation materialization. Do not treat the account upload quota as proof of unlimited individual-parser capacity.

`node tools/generate-import-fixtures.mjs 10000 artifacts/generated-conversations.json` generates 10,000 deterministic conversations and 30,000 messages without accessing any real tool history; the generator supports up to one million conversations. Streaming tests exercise 10,000 records. Versioned native graph expectations are stored in `fixtures/golden`; fidelity, branching, tool pairing, stale conflicts, anchor migration/orphaning, authenticated source encryption, and discovery authorization have regression tests. Undocumented providers remain visibly pending sample validation in the capability registry rather than being mislabeled supported.
