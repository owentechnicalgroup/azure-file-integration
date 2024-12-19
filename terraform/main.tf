terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "azuread" {}

# Resource Group
resource "azurerm_resource_group" "rg" {
  name     = "${var.project_name}-${var.environment}-rg"
  location = var.location
  tags     = var.tags
}

# Service Bus
resource "azurerm_servicebus_namespace" "sb" {
  name                = "${var.project_name}${var.environment}bus"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "Standard"
  tags                = var.tags
}

# Azure AD Application Registration for FileWatcher
resource "azuread_application" "filewatcher" {
  display_name = "FileWatcher"
  owners       = [data.azuread_client_config.current.object_id]
}

# Create service principal for the FileWatcher application
resource "azuread_service_principal" "filewatcher" {
  client_id = azuread_application.filewatcher.client_id
  owners        = [data.azuread_client_config.current.object_id]
}

# Create client secret for FileWatcher
resource "azuread_application_password" "filewatcher" {
  application_id = azuread_application.filewatcher.id
  display_name         = "FileWatcher Secret"
  end_date            = "2024-12-31T23:59:59Z"
}

# Azure AD Application Registration for FileReceiver
resource "azuread_application" "filereceiver" {
  display_name = "FileReceiver"
  owners       = [data.azuread_client_config.current.object_id]
}

# Create service principal for the FileReceiver application
resource "azuread_service_principal" "filereceiver" {
  client_id = azuread_application.filereceiver.client_id
  owners        = [data.azuread_client_config.current.object_id]
}

# Create client secret for FileReceiver
resource "azuread_application_password" "filereceiver" {
  application_id = azuread_application.filereceiver.id
  display_name         = "FileReceiver Secret"
  end_date            = "2024-12-31T23:59:59Z"
}

# Get current Azure AD configuration
data "azuread_client_config" "current" {}

# Log Analytics
resource "azurerm_log_analytics_workspace" "law" {
  name                = "${var.project_name}-${var.environment}-law"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

# Application Insights
resource "azurerm_application_insights" "appinsights" {
  name                = "${var.project_name}-${var.environment}-ai"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  workspace_id        = azurerm_log_analytics_workspace.law.id
  application_type    = "web"
  tags                = var.tags
}

# Service Bus Queue (created after namespace)
resource "azurerm_servicebus_queue" "queue" {
  name         = var.queue_name
  namespace_id = azurerm_servicebus_namespace.sb.id

  partitioning_enabled    = true
  max_size_in_megabytes  = 5120
  default_message_ttl    = "P14D" # 14 days

  depends_on = [azurerm_servicebus_namespace.sb]
}

# Role assignments (created after queue)
resource "azurerm_role_assignment" "servicebus_sender" {
  scope                = azurerm_servicebus_queue.queue.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azuread_service_principal.filewatcher.object_id

  depends_on = [azurerm_servicebus_queue.queue]
}

resource "azurerm_role_assignment" "servicebus_receiver" {
  scope                = azurerm_servicebus_queue.queue.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azuread_service_principal.filereceiver.object_id

  depends_on = [azurerm_servicebus_queue.queue]
}

# Outputs
output "servicebus_namespace" {
  value = azurerm_servicebus_namespace.sb.name
}

output "queue_name" {
  value = azurerm_servicebus_queue.queue.name
}

output "appinsights_connection_string" {
  value     = azurerm_application_insights.appinsights.connection_string
  sensitive = true
}

output "appinsights_instrumentation_key" {
  value     = azurerm_application_insights.appinsights.instrumentation_key
  sensitive = true
}

# FileWatcher Azure AD Application outputs
output "filewatcher_client_id" {
  value = azuread_application.filewatcher.client_id
}

output "filewatcher_client_secret" {
  value     = azuread_application_password.filewatcher.value
  sensitive = true
}

# FileReceiver Azure AD Application outputs
output "filereceiver_client_id" {
  value = azuread_application.filereceiver.client_id
}

output "filereceiver_client_secret" {
  value     = azuread_application_password.filereceiver.value
  sensitive = true
}

output "azure_tenant_id" {
  value = data.azuread_client_config.current.tenant_id
}

output "servicebus_fully_qualified_namespace" {
  value = "${azurerm_servicebus_namespace.sb.name}.servicebus.windows.net"
}
