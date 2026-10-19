using '../main.bicep'

param environmentName = 'prod'
param appServiceSku = 'P1v3'
param sqlSkuName = 'GP_S_Gen5_1'
param sqlSkuTier = 'GeneralPurpose'
param sqlAdminGroupName = 'sg-policies-sql-admins-prod'
// Placeholder ids: the real values are not committed. The pipeline overrides them from the environment's variable group.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
// Not a secret, so committed for each environment: the address range of the policies-prod-vnet agents; the 1433 allow rule admits only this range.
// Replace the placeholder with the real, narrow range (the agent subnet) before the first deployment; never 0.0.0.0/0 or a /8.
param agentAddressPrefix = '10.30.0.0/24'