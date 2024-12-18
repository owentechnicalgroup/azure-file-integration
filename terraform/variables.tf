variable "project_name" {
  description = "Name of the project"
  type        = string
  default     = "fileintegration"
}

variable "environment" {
  description = "Environment (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus2"  # Changed from eastus to eastus2
}

variable "queue_name" {
  description = "Name of the Service Bus queue"
  type        = string
  default     = "file-events"
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default = {
    Environment = "dev"
    Project     = "File Integration"
    ManagedBy   = "Terraform"
  }
}
