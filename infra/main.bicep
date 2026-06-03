// ============================================================================
// main.bicep — Infraestrutura Azure para FidelidadeTransacao
//
// Recursos provisionados:
//   - Azure Container Registry (ACR)
//   - Azure Container Apps + Environment + Log Analytics
//   - Azure SQL Server + Database (primário)
//   - Azure Service Bus Namespace + Topics
//   - Azure Key Vault
//   - Azure Application Insights
//
// Deploy:
//   az deployment group create \
//     --resource-group rg-fidelidade-prod \
//     --template-file infra/main.bicep \
//     --parameters @infra/parameters.prod.json
// ============================================================================

@description('Ambiente: dev | staging | prod')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'prod'

@description('Região Azure')
param location string = resourceGroup().location

@description('Prefixo para nomear todos os recursos')
param prefix string = 'fidelidade'

@description('Usuário administrador do SQL Server')
param sqlAdminLogin string

@secure()
@description('Senha do administrador do SQL Server')
param sqlAdminPassword string

@secure()
@description('JWT Secret Key — mínimo 32 caracteres')
param jwtSecretKey string

@description('Origens CORS permitidas (array de URLs)')
param corsAllowedOrigins array = []

// ── Variáveis ─────────────────────────────────────────────────────────────────
var suffix         = '${prefix}-${environment}'
var acrName        = replace('acr${prefix}${environment}', '-', '')
var appEnvName     = 'cae-${suffix}'
var appName        = 'ca-ledger-api-${suffix}'
var sqlServerName  = 'sql-${suffix}'
var sqlDbName      = 'LedgerDb'
var sbNamespace    = 'sb-${suffix}'
var kvName         = 'kv-${suffix}'
var appInsightsName = 'appi-${suffix}'
var logWorkspaceName = 'log-${suffix}'

// ── Log Analytics Workspace ───────────────────────────────────────────────────
resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logWorkspaceName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Application Insights ──────────────────────────────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
    RetentionInDays: 30
  }
}

// ── Azure Container Registry ──────────────────────────────────────────────────
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: true
  }
}

// ── Azure SQL Server ──────────────────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin:         sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion:          '1.2'
    publicNetworkAccess:        'Enabled'
  }
}

// Permite acesso de outros serviços Azure (Container Apps, pipelines)
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress:   '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name:     environment == 'prod' ? 'S2' : 'S0'
    tier:     'Standard'
    capacity: environment == 'prod' ? 50   : 10
  }
  properties: {
    collation:    'Latin1_General_100_CI_AS_SC_UTF8'
    zoneRedundant: false
  }
}

// ── Azure Service Bus ─────────────────────────────────────────────────────────
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: sbNamespace
  location: location
  sku: {
    name: environment == 'prod' ? 'Standard' : 'Basic'
    tier: environment == 'prod' ? 'Standard' : 'Basic'
  }
}

resource sbTopicCredited 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = if (environment != 'dev') {
  parent: serviceBus
  name: 'points-credited'
  properties: {
    defaultMessageTimeToLive:          'P14D'
    requiresDuplicateDetection:        true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

resource sbTopicDebited 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = if (environment != 'dev') {
  parent: serviceBus
  name: 'points-debited'
  properties: {
    defaultMessageTimeToLive:          'P14D'
    requiresDuplicateDetection:        true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

resource sbTopicRefunded 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = if (environment != 'dev') {
  parent: serviceBus
  name: 'points-refunded'
  properties: {
    defaultMessageTimeToLive:          'P14D'
    requiresDuplicateDetection:        true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

// ── Azure Key Vault ───────────────────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: kvName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization:     true
    enableSoftDelete:            true
    softDeleteRetentionInDays:   90
    enablePurgeProtection:       true
    publicNetworkAccess:         'Enabled'
  }
}

// Secrets no Key Vault
resource kvSecretJwt 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'JwtSecretKey'
  properties: { value: jwtSecretKey }
}

resource kvSecretSqlWrite 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'SqlWriteConnection'
  properties: {
    value: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=${sqlDbName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};TrustServerCertificate=True;'
  }
}

resource kvSecretSqlRead 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'SqlReadConnection'
  properties: {
    value: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=${sqlDbName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};TrustServerCertificate=True;ApplicationIntent=ReadOnly;'
  }
}

// ── Container Apps Environment ────────────────────────────────────────────────
resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: appEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey:  logWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

// ── Container App — Ledger API ────────────────────────────────────────────────
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external:   true
        targetPort: 8080
        transport:  'http'
        corsPolicy: {
          allowedOrigins: corsAllowedOrigins
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server:            acr.properties.loginServer
          username:          acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name:  'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
    }
    template: {
      containers: [
        {
          name:  'ledger-api'
          image: '${acr.properties.loginServer}/ledger-api:latest'
          resources: {
            cpu:    environment == 'prod' ? '1.0'  : '0.5'
            memory: environment == 'prod' ? '2Gi'  : '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT',               value: environment == 'prod' ? 'Production' : 'Staging' }
            { name: 'ASPNETCORE_URLS',                       value: 'http://+:8080' }
            { name: 'ApplicationInsights__ConnectionString', value: appInsights.properties.ConnectionString }
            { name: 'OpenTelemetry__ServiceName',            value: 'Ledger.API' }
            { name: 'OpenTelemetry__ServiceVersion',         value: '1.0.0' }
            // Strings sensíveis vêm do Key Vault via referências secretas
            // Configure via: az containerapp secret set
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 10
              periodSeconds:       30
              failureThreshold:    3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 5
              periodSeconds:       10
              failureThreshold:    3
            }
          ]
        }
      ]
      scale: {
        minReplicas: environment == 'prod' ? 2 : 1
        maxReplicas: environment == 'prod' ? 10 : 3
        rules: [
          {
            name: 'http-scaling'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

// Permissão do Container App para ler secrets do Key Vault
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, containerApp.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId:      containerApp.identity.principalId
    principalType:    'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output acrLoginServer    string = acr.properties.loginServer
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output sqlServerFqdn    string = sqlServer.properties.fullyQualifiedDomainName
output keyVaultUri      string = keyVault.properties.vaultUri
output appInsightsKey   string = appInsights.properties.InstrumentationKey
