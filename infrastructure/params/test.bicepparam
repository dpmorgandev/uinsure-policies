using '../main.bicep'

param environmentName = 'test'
param appServiceSku = 'B1'
param sqlSkuName = 'Basic'
param sqlSkuTier = 'Basic'
param sqlAdminGroupName = 'sg-policies-sql-admins-test'
// Placeholder ids: the real values are not committed. The pipeline overrides them from the environment's variable group.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'