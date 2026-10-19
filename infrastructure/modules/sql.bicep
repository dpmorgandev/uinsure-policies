param suffix string
param location string
param tags object
param skuName string
param skuTier string
param adminGroupName string
param adminGroupObjectId string
param tenantId string

// Entra-only authentication: no administratorLogin or password exists, so there is no SQL secret.
resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: adminGroupName
      sid: adminGroupObjectId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: 'policies'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
}

// Lets Azure-hosted services (the App Service) reach the server.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
