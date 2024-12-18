# Infrastructure as Code - Azure File Integration

This directory contains Terraform configurations to deploy the required Azure resources for the File Integration service.

## Resources Created

- Azure Service Bus Namespace and Queue
- Log Analytics Workspace
- Application Insights

## Prerequisites

1. [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
2. [Terraform](https://www.terraform.io/downloads.html)
3. Azure Subscription
4. Azure Storage Account for Terraform state (see Backend Configuration below)

## Backend Configuration

Before running Terraform, you need to set up an Azure Storage account for the Terraform state:

```bash
# Login to Azure
az login

# Create resource group
az group create --name terraform-state-rg --location eastus

# Create storage account
az storage account create \
  --name <unique-storage-account-name> \
  --resource-group terraform-state-rg \
  --location eastus \
  --sku Standard_LRS

# Create container
az storage container create \
  --name tfstate \
  --account-name <storage-account-name>
```

Then update `backend.tf` with your storage account details.

## Deployment Steps

1. Initialize Terraform:
```bash
terraform init
```

2. Review the planned changes:
```bash
terraform plan
```

3. Apply the changes:
```bash
terraform apply
```

## Outputs

After successful deployment, Terraform will output:
- Service Bus connection string
- Queue name
- Application Insights connection string and instrumentation key

These values should be used to update the FileWatcherService's configuration.

## Clean Up

To remove all created resources:
```bash
terraform destroy
```

## Variables

Customize the deployment by modifying `terraform.tfvars` or providing variables at runtime:

- `project_name`: Name prefix for resources
- `environment`: Deployment environment (dev, staging, prod)
- `location`: Azure region
- `queue_name`: Name of the Service Bus queue
- `tags`: Resource tags
