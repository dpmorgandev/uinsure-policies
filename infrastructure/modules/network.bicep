param suffix string
param location string
param tags object

@description('Address range of the self-hosted deployment agents that run the migrations. Allowed to reach SQL on 1433.')
param agentAddressPrefix string

var appIntegrationPrefix = '10.20.1.0/24'
var privateEndpointPrefix = '10.20.2.0/24'

// The app integration subnet takes no inbound traffic: App Service makes outbound calls only.
resource appIntegrationNsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-app-integration-${suffix}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'deny-all-inbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// SQL is reached on 1433 from the app and the deployment agents only; everything else inbound is denied.
resource privateEndpointNsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'nsg-private-endpoints-${suffix}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'allow-sql-from-app-and-agents'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefixes: [appIntegrationPrefix, agentAddressPrefix]
          sourcePortRange: '*'
          destinationAddressPrefix: privateEndpointPrefix
          destinationPortRange: '1433'
        }
      }
      {
        name: 'deny-all-inbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

// The app reaches SQL over this network only: SQL has no public endpoint.
resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'vnet-${suffix}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.20.0.0/16'] }
    subnets: [
      {
        name: 'snet-app-integration'
        properties: {
          addressPrefix: appIntegrationPrefix
          networkSecurityGroup: { id: appIntegrationNsg.id }
          delegations: [
            {
              name: 'appservice'
              properties: { serviceName: 'Microsoft.Web/serverFarms' }
            }
          ]
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: privateEndpointPrefix
          // Makes the NSG apply to the private endpoint's traffic.
          privateEndpointNetworkPolicies: 'Enabled'
          networkSecurityGroup: { id: privateEndpointNsg.id }
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output appIntegrationSubnetId string = vnet.properties.subnets[0].id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
