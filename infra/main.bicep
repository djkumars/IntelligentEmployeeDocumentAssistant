targetScope = 'subscription'

param environmentName string
param location string

var resourceSuffix = take(uniqueString(subscription().id, environmentName, location), 6)
var resourceGroupName = 'rg-${environmentName}-${resourceSuffix}'
var tags = {
  'azd-env-name': environmentName
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module resources './modules/resources.bicep' = {
  name: 'resources-${resourceSuffix}'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    resourceSuffix: resourceSuffix
    tags: tags
  }
}

output AZURE_RESOURCE_GROUP string = resourceGroup.name
output AZURE_RESOURCE_GROUP_ID string = resourceGroup.id
output API_URL string = resources.outputs.apiUrl
output WEB_URL string = resources.outputs.webUrl
