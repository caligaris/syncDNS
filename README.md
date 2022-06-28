# Azuer VMSS instances private IPs synced to Azure private DNS Zone.

**Overview**: 

- [Requirements](#requirements)
- [Step 1: Deploy resources to Azure](#step-1-deploy-resources-to-azure)
- [Step 2: Setup application configuration](#step-2-setup-application-configuration)
- [Step 3: Setup VMSS Alerts](#step-3-setup-vmss-alerts)
- [Step 4: Scale VMSS](#step-4-scale-vmss)


## Objectives 

Given the following scenario: *Obtain the private IP addresses for all instances belonging to a specific **Azure Virtual Machine Scale Set** in a single DNS query.* 

There are different ways to achieve this goal using REST, CLI, and PowerShell, but currently there is no native support for querying a domain name and getting the IP addresses of all the instances in the scale set.

This is a simple example of how to use VMSS alerts, Azure functions and Private DNS to achieve the goal.

**Note: this code is presented as is and has not been tested for production use**

## Requirements

- You’ll need an existing VMSS, it can be a regular or an AKS cluster as well.

- You’ll also need an Azure Private DNS Zone where the Azure function will keep the list of IPs in sync.

## Step 1: Deploy resources to Azure 

There are multiple ways to deploy a function app into Azure, you can follow these instructions on how to setup [github actions]( https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-github-actions?tabs=dotnet).

For deploying directly from VS Code you can refer to this [documentation](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-vs-code?tabs=csharp#republish-project-files).

## Step 2: Setup application configuration

Once the function app is deployed into Azure you’ll need to setup the following application settings in the Azure function resource:
- DNS_ZONE_RESOURCEID: /subscriptions/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx/resourceGroups/xxxxxxxx/providers/Microsoft.Network/privateDnsZones/\<privateDnsZoneName>
- A_RECORD_NAME: \<subdomain>

The Azure function makes use of Azure managed identities, please enable system assigned identity by following [this instructions](https://docs.microsoft.com/en-us/azure/app-service/overview-managed-identity?tabs=portal%2Chttp#add-a-system-assigned-identity).

The newly created system assigned identity will need to have **Reader** role on the VMSS resource and **Contributor** role on the Private DNS Zone resource. To assign roles to a managed identity refer to [this instructions](https://docs.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/howto-assign-access-portal).

## Step 3: Setup VMSS Alerts

The trigger that will synchronize the list of IP addresses will be an **Alert rule**, as a matter of fact it will be 2 alert rules need to cover the addition of VM instances during scale out as well as deletion of VM instances during a scale in action.

Follow [these steps](https://docs.microsoft.com/en-us/azure/azure-monitor/alerts/alerts-activity-log) to create an Activity log alert. The 2 alert rules will be almost Identical, when setting up the alert under the **Condition** tab select Signal Type: **Activity Log**, then under Signal Name: **Create or Update Virtual Machine Scale Set (Microsoft.Compute/virtualMachineScaleSets)** for rule 1 and **Delete Virtual Machines in a Virtual Machine Scale Set (Microsoft.Compute/virtualMachineScaleSets)** for rule 2.

From this point forward the Alert rule setup will be the same for either rule.

Still in under the **Condition** tab once the Signal is selected under the Alert logic section choose Event Level: **Informational** Status: **Succeeded**

Navigate to the Actions tab, and create a new action group with Action Type as **Azure Function** pointing to the newly deployed Azure function; for a detailed explanation on [create action groups](https://docs.microsoft.com/en-us/azure/azure-monitor/alerts/action-groups?WT.mc_id=Portal-Microsoft_Azure_Monitoring) this is the link for the official docs.

Complete the rest of the alert rule setup and create.

## Step 4: Scale VMSS

To test everything just scale the VMSS resource either in or out. After success of the scale operation the Private DNS Zone subdomain should be updated.

In case of an AKS cluster never scale the VMSS directly, instead use the command `az aks scale`. 

