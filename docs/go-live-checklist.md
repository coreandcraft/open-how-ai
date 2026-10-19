# Go-live checklist: Azure App Service

This is the release gate for the first production deployment. Repository wiring is implemented; unchecked items require verification in the target Azure subscription. The workflow does not provision infrastructure or populate secrets.

## 1. Azure resources and capacity

- [ ] Select subscription, resource group, region, production names, budget, and owner/on-call contact.
- [ ] Provision `infra/main.bicep` using an operator identity allowed to create resources and role assignments:

  ```powershell
  az deployment group create --resource-group YOUR-RG --template-file infra/main.bicep --parameters appName=YOUR-APP storageName=YOURSTORAGE keyVaultName=YOUR-VAULT
  ```

- [ ] Confirm .NET 10 Linux runtime availability in the selected App Service region; `az webapp list-runtimes --os linux` lists available runtimes. Select a supported paid plan with Always On (B1 is the template default); size memory/disk for actual import workloads.
- [ ] Provision Azure Database for PostgreSQL Flexible Server, database `journey`, TLS verification, backups/PITR, and a database user with the DDL privileges needed for startup migrations. Choose PostgreSQL 18. Local Aspire remains PostgreSQL 17.
- [ ] Configure database network access from App Service. Prefer VNet integration/private access; if using public access, restrict firewall rules to the required outbound IPs rather than allowing every Azure service.
- [ ] Review storage/Key Vault network restrictions. The template uses authenticated public endpoints, with anonymous blobs and storage shared keys disabled. Add private endpoints, private DNS, and App Service VNet integration if required by your environment.
- [ ] Keep App Service at **one instance**. In-process import jobs, presence, and rate limits are not coordinated across replicas. Before scaling, implement distributed job claiming and a SignalR service/backplane.
- [ ] Confirm the `files` and `data-protection` blob containers exist and are private; verify versioning and 30-day soft-delete policies. Choose LRS/ZRS/GRS and retention to match recovery requirements.

## 2. Secrets and managed identity

- [ ] Confirm the app's system-assigned identity has **Key Vault Secrets User**, **Key Vault Crypto User**, and **Storage Blob Data Contributor** at the intended resource scopes. Bicep supplies these assignments; allow time for RBAC propagation.
- [ ] Grant the secret-provisioning operator separate permission to set secrets. The app and GitHub deployment identity do not need that permission.
- [ ] Populate the app-specific Key Vault with `ConnectionStrings--Journey`, using the PostgreSQL connection string with TLS and hostname verification (`SSL Mode=VerifyFull`). Never store it in appsettings or GitHub.
- [ ] Populate `Email--Username` and `Email--Password` for SMTP when required by your provider. `Email:Host`, `Port`, and `From` are nonsecret settings.
- [ ] If enabling social login, populate `Authentication--Google--ClientId`, `Authentication--Google--ClientSecret`, `Authentication--Facebook--AppId`, and `Authentication--Facebook--AppSecret` as applicable. Configure provider consent and exact HTTPS callback URLs.
- [ ] If application-level import encryption is enabled, set `Imports--EncryptionKey` to a base64 32-byte key. Preserve the key for existing files; the current format does not store key versions, so do not rotate it without a data re-encryption plan. Azure Storage encryption at rest is available without this optional layer.
- [ ] Keep the data-protection key identifier versionless (`https://VAULT.vault.azure.net/keys/data-protection`). Retain historical Key Vault key versions so old authentication cookies and account tokens remain decryptable.
- [ ] Test managed-identity access to both services and a restart with existing data-protection keys. Secret loading fails startup if the vault is inaccessible. Restart App Service after changing secrets; automatic refresh is not configured.

## 3. App configuration and GitHub credentials

- [ ] Create the GitHub **production** environment, restrict deployment branches to `main`, and configure required reviewers where your GitHub plan supports them.
- [ ] Create a Microsoft Entra application/service principal (or user-assigned managed identity) for GitHub deployment. Add an OIDC federated credential with issuer `https://token.actions.githubusercontent.com`, subject `repo:OWNER/REPOSITORY:environment:production`, and audience `api://AzureADTokenExchange`.
- [ ] Give that deployment principal **Website Contributor** scoped to this App Service. Provision infrastructure separately using an operator with the broader required rights. No publish profile or client secret is used; basic publishing authentication is disabled by Bicep.
- [ ] Set these nonsecret GitHub **production environment variables**:
  - `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`: deployment identity identifiers.
  - `AZURE_WEBAPP_NAME`: App Service name.
  - `PUBLIC_URL`: public HTTPS origin, without a path (custom domain or the default App Service hostname).
  - `KEY_VAULT_URI`: Bicep `keyVaultUri` output.
  - `STORAGE_CONTAINER_URI`: Bicep `storageContainerUri` output.
  - `DATA_PROTECTION_BLOB_URI`: Bicep `dataProtectionBlobUri` output.
  - `DATA_PROTECTION_KEY_URI`: Bicep `dataProtectionKeyUri` output.
  - `EMAIL_HOST`, `EMAIL_FROM`: SMTP host and verified sender address. Port defaults to 587 in appsettings.
- [ ] Review `.github/workflows/deploy-azure.yml`. Builds run on pushes to `main` or manual dispatch; the protected deploy job writes nonsecret variables into the published `appsettings.Production.json`. Application secrets are loaded only by App Service from Key Vault. Do not add credentials to workflow variables.
- [ ] Confirm Production uses `Database:Provider=PostgreSQL`, `Storage:Provider=AzureBlob`, `SeedDemoData=false`, and `Mcp:Enabled=false` unless the documented remote OAuth setup is ready.
- [ ] Confirm HTTPS Only, TLS 1.2+, Always On, WebSockets, hostname restrictions, and App Service forwarded-header integration. Test for HTTPS redirect loops and correct secure cookies behind the actual proxy.

## 4. Data and files

- [ ] Review both provider migrations, especially the PostgreSQL baseline and `ExternalArtifactStorage`. Back up the target database before deploying. Startup currently performs migrations before the app becomes healthy.
- [ ] Decide whether this is a fresh launch or an existing-data migration. SQLite data is **not** automatically copied to PostgreSQL.
- [ ] For existing local sources, copy each `ID.source` to `files/imports/ID.source` and each local `artifacts/ID` to `files/artifacts/ID`, preserving bytes, IDs, hashes, and encryption state before switching providers. Reconcile database rows and blob counts.
- [ ] Existing database-backed imports and attachments retain legacy reads. For a strict all-files-in-Blob release, backfill those bytes to blobs and update `ImportSource.StoredExternally` / `ArtifactBlob.StorageKey`, then clear database payloads only after verifying hashes and downloads. This backfill is an explicit migration, not automatic.
- [ ] Confirm new imports, extracted entries, private assets, and annotation attachments write only to private blobs; downloads must still pass workspace/journey authorization. Verify byte-for-byte download, range requests, and cross-workspace denial.
- [ ] Confirm source retention deletes the correct blobs while preserving normalized messages; reconcile orphaned blobs after interrupted uploads. Blob storage and PostgreSQL do not share a transaction.
- [ ] Treat deployed JS/CSS/images as versioned application assets; they stay in the deployment package. Temporary multipart/parser/decryption files use disposable local scratch space. Size that space and verify it is not relied on for durability.

## 5. Product and release validation

- [ ] Approve and pass the workload envelope in [the scaling decision report](scaling-strategy.md) and [the phase 5 pin](performance/release-envelope.md). The local pilot (phases 0–5) does not certify large-project Azure capacity. Azure 30-minute / 2-hour soak, failover, Blob, rolling deploy and a live canary remain unchecked.
- [ ] Run backend tests, frontend build, and browser end-to-end tests against the intended production-equivalent stack.
- [ ] Test registration, confirmation, password recovery, MFA/recovery codes, login/logout, and cookie persistence across restart/deployment. Confirm SPF, DKIM, DMARC, SMTP connectivity, and delivered mail.
- [ ] Validate custom domain DNS and TLS certificate, canonical URL, sitemap, robots, branding, privacy/terms, and support/contact links. Never launch with a demo account or production demo seeding.
- [ ] Verify import preview, confirmation, cancellation/retry, encrypted source download (if enabled), annotation upload, publication links, and private journey access with PostgreSQL and Azure Blob Storage.
- [ ] Test upload size and duration through the actual App Service ingress. The current app advertises 15 GB/20 GB batch quotas, while multipart/parser memory and upstream limits still apply. Do not advertise those capacities until proven; larger uploads may require direct-to-Blob/resumable upload work.
- [ ] Load-test concurrent imports and the 50 MiB attachment path. Set capacity limits based on measured memory, scratch disk, latency, and restart behavior.
- [ ] Keep experimental MCP disabled unless the issuer, audience, claims, consent, and subject bindings in `deployment.md` are configured and tested.

## 6. Operations, rollout, and rollback

- [ ] Confirm the Bicep deployment created the Log Analytics workspace and Application Insights component, and that App Service has `APPLICATIONINSIGHTS_CONNECTION_STRING`. Production startup requires that setting. Request traces omit query strings and invitation tokens and skip `/api/health`. Do not add platform HTTP logs that store raw URLs. The template alerts `felipe.ramos@fitcode.io` when App Service plan CPU or memory, or PostgreSQL CPU, memory, or storage, averages above 80% for 15 minutes. Confirm that mailbox accepted the Azure Monitor action-group email. Still configure alerts for 5xx, failed imports, storage or database access failures, cost, uptime checks, and an on-call path.
- [ ] Test restoring PostgreSQL and matching blob versions together. Record RPO/RTO and retain data-protection blobs and Key Vault key versions in the recovery plan.
- [ ] Schedule a maintenance window for the first direct deployment. Drain/finish active imports before restart. The YAML does not provide slot swaps or distributed worker draining.
- [ ] Trigger the workflow, review the deployment gate, verify `/api/health`, then run the authenticated and file-storage smoke tests above. The HTTP health check alone is not a full release acceptance test.
- [ ] Keep the previous application artifact and its nonsecret settings. For rollback, redeploy that version only if its schema compatibility is confirmed; database rollback requires the reviewed restore/migration plan. Never assume deploying old binaries reverses migrations.
- [ ] Record deployed commit, migration IDs, release owner, verified checklist results, and any accepted limitations.

## References

- [GitHub OIDC with Azure](https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- [Deploy App Service with GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions)
- [ASP.NET Core Key Vault configuration](https://learn.microsoft.com/en-us/aspnet/core/security/key-vault-configuration)
- [Data protection with Azure Blob Storage and Key Vault](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview)

## Optional GitHub repository integration

Follow the [GitHub App setup and operations runbook](github-integration.md) to register the read-only repository App, provision its PEM in Key Vault, configure App Service/Bicep, install it on selected repositories, and complete credential plus file-preview acceptance checks. This App is separate from the GitHub Actions OIDC deployment identity above.
