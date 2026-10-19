param suffix string
param location string
param tags object
param skuName string
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string
param appIntegrationSubnetId string

// User-assigned so its clientId is known at deployment time: the pipeline creates the database user from it.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${suffix}'
  location: location
  tags: tags
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${suffix}'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: skuName }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${suffix}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: appIntegrationSubnetId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/ready'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Database__Provider', value: 'SqlServer' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ]
      // The app's user-assigned identity signs in to SQL by client id: no password in the string.
      connectionStrings: [
        {
          name: 'Policies'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;'
        }
      ]
    }
  }
}

// Nothing needs basic-auth publishing: the pipeline deploys under an Entra service connection.
resource scmCredentialsPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}

resource ftpCredentialsPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

output appName string = app.name
output hostName string = app.properties.defaultHostName
output clientId string = identity.properties.clientId
