terraform {
  backend "azurerm" {
    resource_group_name  = "fileintegration-dev-rg"
    storage_account_name = "fileintdevtfstate"
    container_name       = "tfstate"
    key                 = "fileintegration.dev.tfstate"
  }
}
