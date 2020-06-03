# Model Trainer

The model trainer is a console application that helps with taking the output of the custom labelling tool, uploading to azure and training the model.  Its a pretty simple
application where behaviour can be confiured using command line switches.

![ModelTrainerHelp](../docs/ModelTrainer-help.jpg)

Once invoices for a document format have been sucessfully labelled using the [sample labelling tool](https://docs.microsoft.com/en-us/azure/cognitive-services/form-recognizer/quickstarts/label-tool) and we are happy with the 
accuracy of the model, we need to move the newly trained model into 'production' so that it can be used to recognise invoices through the pipeline.

The training assets from the labelling tool should be gathered together into a single directory on the local hard drive.  Run the ModelTrainer with coreect parameters to pick up the assets, specify the document format to which the assets relate, and specify the location to upload the assets in azure. 
This location wil be the one where the __Trainer__ function is expecting to find them.  The --blobContainerFolder specifies a subfolder within the upload container (and allows the assets to be grouped together).  

__Warning:__ if assets from a previous run exist in the --blobContainerFolder from a previous run they will be deleted. This folder can be any name that follows normal azure storage naming rules.  Therefore you could increment this folder name each time, and keep the assets for each version of the model for history or retention reasons.

Once all the new assets are uploaded, then a record is written to an Azure storage queue to trigger onward processing.  This queue (default name=training) can be overridden in the appsettings.local.json in the directory the exe file is running from.  If the queue does not exist it will be created.  (This supports running multiple (test/uat/live) environments side by side).