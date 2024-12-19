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
