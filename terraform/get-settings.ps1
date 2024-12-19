# Get values from Terraform
$appInsightsConnStr = terraform output -raw appinsights_connection_string
$serviceBusNamespace = terraform output -raw servicebus_fully_qualified_namespace
$queueName = terraform output -raw queue_name
$tenantId = terraform output -raw azure_tenant_id

# FileWatcher values
$watcherClientId = terraform output -raw filewatcher_client_id
$watcherClientSecret = terraform output -raw filewatcher_client_secret

# FileReceiver values
$receiverClientId = terraform output -raw filereceiver_client_id
$receiverClientSecret = terraform output -raw filereceiver_client_secret

# Update FileWatcher settings
$watcherSettings = Get-Content -Path "../FileWatcherService/appsettings.template.json" | ConvertFrom-Json
$watcherSettings.ApplicationInsights.ConnectionString = $appInsightsConnStr
$watcherSettings.ServiceBusConfig.FullyQualifiedNamespace = $serviceBusNamespace
$watcherSettings.ServiceBusConfig.QueueName = $queueName
$watcherSettings.AzureAd.ClientId = $watcherClientId
$watcherSettings.AzureAd.ClientSecret = $watcherClientSecret
$watcherSettings.AzureAd.TenantId = $tenantId

$watcherSettings | ConvertTo-Json -Depth 10 | Set-Content -Path "../FileWatcherService/appsettings.json"

# Update FileReceiver settings
$receiverSettings = Get-Content -Path "../FileReceiverService/appsettings.template.json" | ConvertFrom-Json
$receiverSettings.ApplicationInsights.ConnectionString = $appInsightsConnStr
$receiverSettings.ServiceBusConfig.FullyQualifiedNamespace = $serviceBusNamespace
$receiverSettings.ServiceBusConfig.QueueName = $queueName
$receiverSettings.AzureAd.ClientId = $receiverClientId
$receiverSettings.AzureAd.ClientSecret = $receiverClientSecret
$receiverSettings.AzureAd.TenantId = $tenantId

$receiverSettings | ConvertTo-Json -Depth 10 | Set-Content -Path "../FileReceiverService/appsettings.json"

Write-Host "Settings files have been updated successfully."
