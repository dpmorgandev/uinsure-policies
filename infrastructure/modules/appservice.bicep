param suffix string
param location string
param tags object
param skuName string
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string

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
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
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
      // The app's managed identity signs in to SQL: no user name or password in the string.
      connectionStrings: [
        {
          name: 'Policies'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;'
        }
      ]
    }
  }
}

output appName string = app.name
output hostName string = app.properties.defaultHostName
output principalId string = app.identity.principalId
