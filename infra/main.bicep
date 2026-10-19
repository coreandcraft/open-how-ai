targetScope = 'resourceGroup'

param location string = resourceGroup().location
param appName string
param githubEnabled bool = false
@allowed(['Public', 'App'])
param githubAuthenticationMode string = 'Public'
param githubAppClientId string = ''
param githubAppSlug string = ''
param storageName string
param keyVaultName string
param planSku string = 'S1'
param postgresServerName string
param postgresAdminLogin string = 'journeyadmin'
@secure()
param postgresAdminPassword string
param postgresSku string = 'Standard_B2s'
param postgresTier string = 'Burstable'
param postgresStorageGb int = 32
param alertEmail string = 'felipe.ramos@fitcode.io'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-plan'
  location: location
  sku: { name: planSku, capacity: 1 }
  kind: 'linux'
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Preview uploads, progress and temporary results are owned by one app instance.
    clientAffinityEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Journey.Server.dll'
      alwaysOn: true
      webSocketsEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/api/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        // App Service's managed reverse proxy terminates TLS.
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
        { name: 'GitHub__Enabled', value: string(githubEnabled) }
        { name: 'GitHub__AuthenticationMode', value: githubAuthenticationMode }
        { name: 'GitHub__App__ClientId', value: githubAppClientId }
        { name: 'GitHub__App__Slug', value: githubAppSlug }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
    }
  }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}
resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 30 }
    containerDeleteRetentionPolicy: { enabled: true, days: 30 }
  }
}
resource files 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'files'
  properties: { publicAccess: 'None' }
}
resource keys 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'data-protection'
  properties: { publicAccess: 'None' }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
  }
}
resource protectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'data-protection'
  properties: { kty: 'RSA', keySize: 2048, keyOps: ['wrapKey', 'unwrapKey'] }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  sku: { name: postgresSku, tier: postgresTier }
  properties: {
    version: '18'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: postgresStorageGb, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}
resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'journey'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}
// The library search migration creates pg_trgm indexes; Azure only allows listed extensions.
resource postgresExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: { value: 'PG_TRGM', source: 'user-override' }
}
// 0.0.0.0 is Azure's convention for "services hosted in Azure"; it does not open the server publicly.
resource postgresAzureAccess 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}
resource connectionString 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'ConnectionStrings--Journey'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${postgresDb.name};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=VerifyFull'
  }
}

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, app.id, 'blob-contributor')
  scope: storage
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}
resource secretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, app.id, 'secrets-user')
  scope: vault
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}
resource keyRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, app.id, 'crypto-user')
  scope: vault
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
  }
}

// Workspace names allow 4–63 characters. App Service names can be 60, so the suffix must be truncated.
var telemetryName = take(appName, 58)

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${telemetryName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
  }
}

// Sustained capacity, not a one-minute spike. Azure emails alertEmail only after that mailbox confirms the action group.
resource alertGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${telemetryName}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'journey'
    enabled: true
    emailReceivers: [
      {
        name: 'capacity'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

var capacityAlerts = [
  {
    suffix: 'plan-cpu'
    description: 'App Service plan average CPU is above 80%.'
    targetId: plan.id
    metricNamespace: 'Microsoft.Web/serverfarms'
    metricName: 'CpuPercentage'
  }
  {
    suffix: 'plan-memory'
    description: 'App Service plan average memory is above 80%.'
    targetId: plan.id
    metricNamespace: 'Microsoft.Web/serverfarms'
    metricName: 'MemoryPercentage'
  }
  {
    suffix: 'postgres-cpu'
    description: 'PostgreSQL average CPU is above 80%.'
    targetId: postgres.id
    metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
    metricName: 'cpu_percent'
  }
  {
    suffix: 'postgres-memory'
    description: 'PostgreSQL average memory is above 80%.'
    targetId: postgres.id
    metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
    metricName: 'memory_percent'
  }
  {
    suffix: 'postgres-storage'
    description: 'PostgreSQL storage used is above 80%.'
    targetId: postgres.id
    metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
    metricName: 'storage_percent'
  }
]

resource capacityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = [for alert in capacityAlerts: {
  name: '${telemetryName}-${alert.suffix}'
  location: 'global'
  properties: {
    description: alert.description
    severity: 2
    enabled: true
    scopes: [
      alert.targetId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'usage'
          metricNamespace: alert.metricNamespace
          metricName: alert.metricName
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: alertGroup.id
      }
    ]
    autoMitigate: true
  }
}]

output appName string = app.name
output defaultUrl string = 'https://${app.properties.defaultHostName}'
output keyVaultUri string = vault.properties.vaultUri
output applicationInsightsName string = appInsights.name
output alertActionGroupName string = alertGroup.name
output logAnalyticsWorkspaceName string = logs.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output storageContainerUri string = '${storage.properties.primaryEndpoints.blob}files'
output dataProtectionBlobUri string = '${storage.properties.primaryEndpoints.blob}data-protection/keys.xml'
output dataProtectionKeyUri string = '${vault.properties.vaultUri}keys/data-protection'
