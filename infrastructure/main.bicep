targetScope = 'subscription'

@description('Environment short name.')
@allowed(['dev', 'test', 'prod'])
param environmentName string

@description('Azure region for every resource.')
param location string = 'uksouth'

@description('Workload name used in resource names.')
@minLength(3)
@maxLength(12)
param workload string = 'policies'

@description('App Service plan SKU name.')
param appServiceSku string

@description('SQL database SKU name.')
param sqlSkuName string

@description('SQL database SKU tier.')
param sqlSkuTier string

@description('Display name of the Entra group that administers the SQL server.')
param sqlAdminGroupName string

@description('Object id of the Entra group that administers the SQL server.')
param sqlAdminGroupObjectId string

var suffix = '${workload}-${environmentName}'
var tags = {
  workload: workload
  environment: environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${suffix}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    suffix: suffix
    location: location
    tags: tags
  }
}

module network 'modules/network.bicep' = {
  scope: rg
  name: 'network'
  params: {
    suffix: suffix
    location: location
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  scope: rg
  name: 'sql'
  params: {
    suffix: suffix
    location: location
    tags: tags
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
    vnetId: network.outputs.vnetId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
  }
}

module app 'modules/appservice.bicep' = {
  scope: rg
  name: 'app'
  params: {
    suffix: suffix
    location: location
    tags: tags
    skuName: appServiceSku
    appInsightsConnectionString: monitoring.outputs.connectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    appIntegrationSubnetId: network.outputs.appIntegrationSubnetId
  }
}

output resourceGroupName string = rg.name
output appName string = app.outputs.appName
output appHostName string = app.outputs.hostName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output identityClientId string = app.outputs.clientId
