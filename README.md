# placebo

Lots of documentation to be added.

## Placebo.Trainer Console App

This new console application trains the model without ever downloading the documents from Azure.  Hopefuly it can work directly on the output fromt he labelling tool (but CORS policy may prevent this).
In order to run it:

Placebo.Trainer  --documentFormat alliance  --labellingContainerSasUrl https://<<your-storage-account>>.core.windows.net/training-requests?sp=rl&st=2020-06-15T07:20:... etc. --blobContainerFolder alliance

In order to prepare to run a connection string for the storage account with containing the training queue, and the training queue name (the queue that is being monitored by the training funciton) need to be added to a local configuration file called appsettings.local.json.  If adding to visual studio make sure that this is set to copy-if-newer so that it gets copied to the directory of the binary.  If running from the command line the file needs to added to the bin directory.

{

  "StorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=<<your-storage-account>>;AccountKey=R...th9uFD7W4dm08RAvHhxvPYCGGsqjgMA==;EndpointSuffix=core.windows.net",
  
  "TrainingQueueName": "training"

}

The training assets for a particular document format must be stored within a folder in a container in a storage account.  

The labellingContainerSasUrl parameter must be a SAS token with RL permissions for that container.
