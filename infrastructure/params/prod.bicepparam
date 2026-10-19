using '../main.bicep'

param environmentName = 'prod'
param appServiceSku = 'P1v3'
param sqlSkuName = 'GP_S_Gen5_1'
param sqlSkuTier = 'GeneralPurpose'
param sqlAdminGroupName = 'sg-policies-sql-admins-prod'
// Placeholder ids: the real values are not committed. The pipeline overrides them from the environment's variable group.
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'