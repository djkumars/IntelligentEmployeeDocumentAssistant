targetScope = 'resourceGroup'

param environmentName string
param location string
param resourceSuffix string
param tags object

var safeEnvironmentName = toLower(replace(replace(environmentName, '_', '-'), ' ', '-'))
var shortEnvironmentName = take(replace(safeEnvironmentName, '-', ''), 12)

var webName = take('swa-${safeEnvironmentName}-${resourceSuffix}', 40)
var apiName = take('api-${safeEnvironmentName}-${resourceSuffix}', 60)
var planName = take('asp-${safeEnvironmentName}-${resourceSuffix}', 60)
var foundryName = take('oai-${safeEnvironmentName}-${resourceSuffix}', 64)
var documentIntelligenceName = take('doc-${safeEnvironmentName}-${resourceSuffix}', 64)
var searchName = take('srch-${safeEnvironmentName}-${resourceSuffix}', 60)
var storageName = 'str${shortEnvironmentName}${resourceSuffix}'
var logAnalyticsName = take('log-${safeEnvironmentName}-${resourceSuffix}', 63)
var appInsightsName = 'appi-${safeEnvironmentName}-${resourceSuffix}'
var blobContainerName = 'hr-documents'

var webOrigin = 'https://${staticWebApp.properties.defaultHostname}'
var apiUrl = 'https://${apiName}.azurewebsites.net'
var openAiEndpoint = 'https://${foundryName}.openai.azure.com/'
var searchEndpoint = 'https://${searchName}.search.windows.net'
var blobServiceUri = 'https://${storageName}.blob.${environment().suffixes.storage}'
var documentIntelligenceEndpoint = 'https://${documentIntelligenceName}.cognitiveservices.azure.com/'

var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var searchIndexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a'
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-api-${safeEnvironmentName}-${resourceSuffix}'
  location: location
  tags: tags
}

module web 'br/public:avm/res/web/static-site:0.9.0' = {
  name: 'static-web-app-${resourceSuffix}'
  params: {
    name: webName
    location: location
    sku: 'Free'
    tags: union(tags, {
      'azd-service-name': 'web'
    })
    enableTelemetry: false
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2024-11-01' existing = {
  name: webName
}

module appServicePlan 'br/public:avm/res/web/serverfarm:0.7.0' = {
  name: 'app-service-plan-${resourceSuffix}'
  params: {
    name: planName
    location: location
    kind: 'linux'
    skuName: 'B1'
    skuCapacity: 1
    tags: tags
    enableTelemetry: false
  }
}

module api 'br/public:avm/res/web/site:0.24.0' = {
  name: 'api-app-service-${resourceSuffix}'
  dependsOn: [
    web
    appInsightsModule
  ]
  params: {
    name: apiName
    location: location
    kind: 'app,linux'
    serverFarmResourceId: appServicePlan.outputs.resourceId
    managedIdentities: {
      userAssignedResourceIds: [
        apiIdentity.id
      ]
    }
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      healthCheckPath: '/api/health'
      http20Enabled: true
      linuxFxVersion: 'DOTNETCORE|8.0'
      cors: {
        allowedOrigins: [
          webOrigin
        ]
        supportCredentials: false
      }
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'Assistant__FoundryEndpoint'
          value: openAiEndpoint
        }
        {
          name: 'Assistant__ChatDeployment'
          value: 'gpt-4o-mini'
        }
        {
          name: 'Assistant__EmbeddingDeployment'
          value: 'text-embedding-3-small'
        }
        {
          name: 'Assistant__SearchEndpoint'
          value: searchEndpoint
        }
        {
          name: 'Assistant__SearchIndexName'
          value: 'employee-knowledge'
        }
        {
          name: 'Assistant__BlobServiceUri'
          value: blobServiceUri
        }
        {
          name: 'Assistant__BlobContainerName'
          value: blobContainerName
        }
        {
          name: 'Assistant__DocumentIntelligenceEndpoint'
          value: documentIntelligenceEndpoint
        }
        {
          name: 'Assistant__ManagedIdentityClientId'
          value: apiIdentity.properties.clientId
        }
        {
          name: 'AllowedOrigins__0'
          value: webOrigin
        }
      ]
    }
    tags: union(tags, {
      'azd-service-name': 'api'
    })
    httpsOnly: true
    enableTelemetry: false
  }
}

module foundry 'br/public:avm/res/cognitive-services/account:0.19.0' = {
  name: 'foundry-account-${resourceSuffix}'
  params: {
    name: foundryName
    kind: 'OpenAI'
    sku: 'S0'
    location: location
    customSubDomainName: foundryName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    tags: tags
    enableTelemetry: false
  }
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryName
}

resource documentIntelligenceAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: documentIntelligenceName
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: 'gpt-4o-mini'
  dependsOn: [
    foundry
  ]
  sku: {
    name: 'Standard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      publisher: 'OpenAI'
      version: '2024-07-18'
    }
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: 'text-embedding-3-small'
  dependsOn: [
    foundry
  ]
  sku: {
    name: 'Standard'
    capacity: 1
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      publisher: 'OpenAI'
      version: '1'
    }
  }
}

module documentIntelligence 'br/public:avm/res/cognitive-services/account:0.19.0' = {
  name: 'document-intelligence-${resourceSuffix}'
  params: {
    name: documentIntelligenceName
    kind: 'FormRecognizer'
    sku: 'S0'
    location: location
    customSubDomainName: documentIntelligenceName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    tags: tags
    enableTelemetry: false
  }
}

module search 'br/public:avm/res/search/search-service:0.13.0' = {
  name: 'search-service-${resourceSuffix}'
  params: {
    name: searchName
    location: location
    sku: 'basic'
    replicaCount: 1
    partitionCount: 1
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
    tags: tags
    enableTelemetry: false
  }
}

resource searchService 'Microsoft.Search/searchServices@2025-05-01' existing = {
  name: searchName
}

module storage 'br/public:avm/res/storage/storage-account:0.33.0' = {
  name: 'storage-account-${resourceSuffix}'
  params: {
    name: storageName
    location: location
    skuName: 'Standard_LRS'
    kind: 'StorageV2'
    accessTier: 'Hot'
    defaultToOAuthAuthentication: true
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
    tags: tags
    enableTelemetry: false
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: blobContainerName
  dependsOn: [
    storage
  ]
  properties: {
    publicAccess: 'None'
  }
}

module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.16.0' = {
  name: 'log-analytics-${resourceSuffix}'
  params: {
    name: logAnalyticsName
    location: location
    tags: tags
    enableTelemetry: false
  }
}

module appInsightsModule 'br/public:avm/res/insights/component:0.8.0' = {
  name: 'application-insights-${resourceSuffix}'
  params: {
    name: appInsightsName
    location: location
    applicationType: 'web'
    workspaceResourceId: logAnalytics.outputs.resourceId
    tags: tags
    enableTelemetry: false
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource foundryOpenAiUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryAccount.id, apiIdentity.id, cognitiveServicesOpenAiUserRoleId)
  scope: foundryAccount
  dependsOn: [
    foundry
  ]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource documentIntelligenceUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentIntelligenceAccount.id, apiIdentity.id, cognitiveServicesUserRoleId)
  scope: documentIntelligenceAccount
  dependsOn: [
    documentIntelligence
  ]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource searchIndexDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, apiIdentity.id, searchIndexDataContributorRoleId)
  scope: searchService
  dependsOn: [
    search
  ]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource searchServiceContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, apiIdentity.id, searchServiceContributorRoleId)
  scope: searchService
  dependsOn: [
    search
  ]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageBlobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, apiIdentity.id, storageBlobDataContributorRoleId)
  scope: storageAccount
  dependsOn: [
    storage
  ]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output apiUrl string = apiUrl
output webUrl string = webOrigin
