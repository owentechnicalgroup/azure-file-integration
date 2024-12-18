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

# Storage Account for Terraform State
resource "azurerm_storage_account" "tfstate" {
  name                     = "fileintdevtfstate"  # Shortened name
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = var.tags

  blob_properties {
    versioning_enabled = true
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_name  = azurerm_storage_account.tfstate.name
  container_access_type = "private"
}

# Service Bus
resource "azurerm_servicebus_namespace" "sb" {
  name                = "${var.project_name}${var.environment}bus"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "Standard"
  tags                = var.tags
}

resource "azurerm_servicebus_queue" "queue" {
  name         = var.queue_name
  namespace_id = azurerm_servicebus_namespace.sb.id

  partitioning_enabled    = true
  max_size_in_megabytes  = 5120
  default_message_ttl    = "P14D" # 14 days
}

# Azure AD Application Registration for FileWatcher
resource "azuread_application" "filewatcher" {
  display_name = "FileWatcher"
  owners       = [data.azuread_client_config.current.object_id]
}

# Create service principal for the FileWatcher application
resource "azuread_service_principal" "filewatcher" {
  application_id = azuread_application.filewatcher.application_id
  owners        = [data.azuread_client_config.current.object_id]
}

# Create client secret for FileWatcher
resource "azuread_application_password" "filewatcher" {
  application_object_id = azuread_application.filewatcher.object_id
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
  application_id = azuread_application.filereceiver.application_id
  owners        = [data.azuread_client_config.current.object_id]
}

# Create client secret for FileReceiver
resource "azuread_application_password" "filereceiver" {
  application_object_id = azuread_application.filereceiver.object_id
  display_name         = "FileReceiver Secret"
  end_date            = "2024-12-31T23:59:59Z"
}

# Get current Azure AD configuration
data "azuread_client_config" "current" {}

# Assign Service Bus Data Sender role to the FileWatcher application
resource "azurerm_role_assignment" "servicebus_sender" {
  scope                = azurerm_servicebus_queue.queue.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azuread_service_principal.filewatcher.object_id
}

# Assign Service Bus Data Receiver role to the FileReceiver application
resource "azurerm_role_assignment" "servicebus_receiver" {
  scope                = azurerm_servicebus_queue.queue.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azuread_service_principal.filereceiver.object_id
}

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

output "storage_account_name" {
  value = azurerm_storage_account.tfstate.name
}

output "storage_container_name" {
  value = azurerm_storage_container.tfstate.name
}

# FileWatcher Azure AD Application outputs
output "filewatcher_client_id" {
  value = azuread_application.filewatcher.application_id
}

output "filewatcher_client_secret" {
  value     = azuread_application_password.filewatcher.value
  sensitive = true
}

# FileReceiver Azure AD Application outputs
output "filereceiver_client_id" {
  value = azuread_application.filereceiver.application_id
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
