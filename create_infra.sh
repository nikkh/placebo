#!/bin/bash
if [ -z "$APPLICATION_NAME" ]; then 
    echo "APPLICATION_NAME must contain application root name (4-6 alphanumeric)"
    exit
fi

if [ -z "$LOCATION" ]; then 
    echo "LOCATION does not contain a valid azure location, defaulting to uksouth"
    export LOCATION=uksouth
fi

if [ -z "$ADMIN_PASSWORD" ]; then 
    echo "ADMIN_PASSWORD must contain a valid SQL Server password"
    exit
fi

applicationName=$APPLICATION_NAME
storageAccountName="$applicationName$RANDOM"
stagingStorageAccountName="$storageAccountName"staging
resourceGroupName="$applicationName-rg"
functionAppName="$applicationName-func"
dbServerName="$applicationName-db-server"
databaseName="$applicationName-db"
evtgrdsubName="$applicationName-evt-sub"
adminLogin="$applicationName-admin"
password=$ADMIN_PASSWORD
location=$LOCATION
# Create a resource group
echo "storageAccountName=$storageAccountName"
echo "stagingStorageAccountName=$stagingStorageAccountName"
echo "resourceGroupName=$resourceGroupName"
echo "functionAppName=$functionAppName"
echo "dbServerName=$dbServerName"
echo "databaseName=$databaseName"
echo "evtgrdsubName=$evtgrdsubName"
echo "adminLogin=$adminLogin"
echo "password=$password"
echo "location=$location"
echo -e "\e[33m"
read -n 1 -r -s -p $"Press Enter to create the envrionment or Ctrl-C to quit and change environment variables: "
echo -e "\e[0m"

az group create -n $resourceGroupName -l $location 
# Create a storage account
az storage account create  --name $storageAccountName  --location $location  --resource-group $resourceGroupName  --sku Standard_LRS
# Create a staging storage account
az storage account create  --name $stagingStorageAccountName  --location $location  --resource-group $resourceGroupName  --sku Standard_LRS
# Create a queue in staging storage account
az storage queue create --name incoming --account-name $stagingStorageAccountName
#Create an event grid subscription so that any time a blob is added anywhere on the storage account a message will appear on the queue
stagingStorageAccountId=$(az storage account show -n $stagingStorageAccountName --query id -o tsv)
az eventgrid event-subscription create --name $evtgrdsubName --source-resource-id $stagingStorageAccountId --endpoint-type storagequeue --endpoint $stagingStorageAccountId/queueservices/default/queues/incoming --included-event-types Microsoft.Storage.BlobCreated

# Create the storage containers for processing the documents
az storage container create  --name assets-processing-completed --account-name $storageAccountName --auth-mode login
az storage container create  --name process-exceptions --account-name $storageAccountName --auth-mode login
az storage container create  --name process-in-json --account-name $storageAccountName --auth-mode login
az storage container create  --name recognize-exceptions --account-name $storageAccountName --auth-mode login
az storage container create  --name recognize-in-image --account-name $storageAccountName --auth-mode login
az storage container create  --name training-requests --account-name $storageAccountName --auth-mode login
az storage container create  --name training-assets-x --account-name $storageAccountName --auth-mode login
az storage container create  --name recognize-out-image --account-name $storageAccountName --auth-mode login 

# Create a V3 Function App
az functionapp create  --name $functionAppName   --storage-account $storageAccountName   --consumption-plan-location $(location)   --resource-group $resourceGroupName --functions-version 3
# Create a database server (could we use serverless?)
az sql server create -n $dbServerName -g $resourceGroupName -l $location -u $adminLogin -p $password
# Configure a firewall rule for the server
az sql server firewall-rule create -g $resourceGroupName -s $dbServerName -n DevOpsDefault --start-ip-address "0.0.0.0" --end-ip-address "0.0.0.0"
# Create a sql db
az sql db create -g $resourceGroupName -s $dbServerName -n $databaseName --service-objective S0
