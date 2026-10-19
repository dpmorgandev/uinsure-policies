using '../main.bicep'

param environmentName = 'dev'
param appServiceSku = 'B1'
param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'
param sqlAdminGroupName = 'sg-policies-sql-admins-dev'
// Placeholder ids: the real values are not committed. The pipeline overrides them from the environment's variable group.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
// Placeholder: the real address range of the policies-dev-vnet agent pool is not committed. Set it before the first deployment.
param agentAddressPrefix = '10.30.0.0/24'