using Placebo.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ModelTrainer
{
    class Program
    {
        static string dbConnectionString = "";
        static string storageConnectionString = "";
        static string trainingQueueName = "";

        static async Task<int> Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();
            dbConnectionString = configuration.GetConnectionString("PlaceboDatabase");
            storageConnectionString = configuration["storageConnectionString"];
            trainingQueueName = configuration["TrainingQueueName"];
            var shortDBConnectionString = dbConnectionString.Substring(0, 50);
            var shortStorageConnectionString = storageConnectionString.Substring(0, 50);
            Console.WriteLine($"Database connection string = {shortDBConnectionString}");
            Console.WriteLine($"Storage connection string = {shortStorageConnectionString}");

            Console.WriteLine($"Training Queue Name = {trainingQueueName}");

            var rootCommand = new RootCommand
            {
                new Option<string>(
                    "--documentFormat",
                    description: "The document format for which the training assets should be uploaded"),

                new Option<string>(
                    "--localPath",
                    description: "The local folder containing the training assets"),
                new Option<string>(
                    "--blobContainer",
                    description: "The name of the blob container where the assets should be uploaded"),
                new Option<string>(
                    "--blobContainerFolder",
                    getDefaultValue: () => null,
                    description: "The anme of a folder within the blob container where the assets should be uploaded"),
            };

            rootCommand.Description = "This command uploads a set of model training assets for a document format (e.g. phoenix) from a local directory to Azure blob storage.  " +
                "This triggers a model training run in azure.  A new model is created based on the assets and a record of the new model is kept in the ModelTraining table in the " +
                "database.  This new model becomes the latest model for that document format and is then used by the rcognizer component while processing future documents";
            try
            {
                rootCommand.Handler = CommandHandler.Create<string, string, string, string>(async (documentFormat, localPath, blobContainer, blobContainerFolder) =>
                {
                    try
                    {
                        Console.WriteLine($"The value for --documentFormat is: {documentFormat}");
                        if (string.IsNullOrEmpty(documentFormat))
                        {
                            throw new Exception($"--documentFormat {documentFormat} must be provided");
                        }

                        Console.WriteLine($"The value for --localPath is: {localPath}");
                        if (string.IsNullOrEmpty(localPath))
                        {
                            throw new Exception($"--localPath {localPath} must be provided");
                        }

                        Console.WriteLine($"The value for --blobContainer is: {blobContainer}");
                        if (string.IsNullOrEmpty(blobContainer))
                        {
                            throw new Exception($"--blobContainer {blobContainer} must be provided");
                        }

                        Console.WriteLine($"The value for --blobContainerFolder is: {blobContainerFolder}");
                        if (string.IsNullOrEmpty(blobContainerFolder))
                        {
                            throw new Exception($"--blobContainerFolder {blobContainerFolder} must be provided");
                        }

                        if (!Directory.Exists(localPath))
                        {
                            throw new Exception($"--localPath {localPath} does not exist or is not a directory");
                        }

                        // Get hold of the storage account
                        CloudStorageAccount storageAccount = null;
                        try
                        {
                            storageAccount = CloudStorageAccount.Parse(storageConnectionString);

                            var targetBlobClient = storageAccount.CreateCloudBlobClient();
                            var targetContainer = targetBlobClient.GetContainerReference(blobContainer);
                            await targetContainer.CreateIfNotExistsAsync();
                            var directory = targetContainer.GetDirectoryReference(blobContainerFolder);

                            BlobResultSegment resultSegment = null;
                            BlobContinuationToken continuationToken = null;

                            do
                            {
                                resultSegment = await directory.ListBlobsSegmentedAsync(true, BlobListingDetails.All, 50, continuationToken, null, null);
                                if(resultSegment.Results.Count() > 0)
                                {
                                    Console.WriteLine($"Container already contains {resultSegment.Results.Count()} blobs - they will be deleted");
                                }
                                foreach (var blob in resultSegment.Results)
                                {
                                    try
                                    {
                                        var blobToDelete = directory.GetBlockBlobReference(blob.Uri.ToString());
                                        await blobToDelete.DeleteIfExistsAsync();
                                        Console.WriteLine($"Deleted blob: {blobToDelete.Name}");
                                    }
                                    catch(Exception e)
                                    {
                                        Console.WriteLine("Unable to delete blob {blob.Uri.ToString()}");
                                    }
                                }

                                // Get the continuation token. If not null, get the next segment.
                                continuationToken = resultSegment.ContinuationToken;

                            } while (continuationToken != null);

                            string[] fileEntries = Directory.GetFiles(localPath);
                            Stopwatch innnerTimer = new Stopwatch();
                            Stopwatch outerTimer = new Stopwatch();
                            outerTimer.Start();
                            int i = 0;
                            foreach (string fileName in fileEntries)
                            {
                                FileInfo f = new FileInfo(fileName);
                                innnerTimer.Reset();
                                innnerTimer.Start();
                                using FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                var blobToUpload = directory.GetBlockBlobReference(f.Name);
                                await blobToUpload.UploadFromStreamAsync(fs);
                                i++;
                                innnerTimer.Stop();

                                Console.WriteLine($"Uploaded file {f.Name} to container {targetContainer.Name} in {innnerTimer.ElapsedMilliseconds} ms");

                            }
                            outerTimer.Stop();
                            Console.WriteLine($"Uploaded {i} files to container {targetContainer.Name} in {outerTimer.ElapsedMilliseconds} ms");
                            var policy = new SharedAccessBlobPolicy
                            {
                                Permissions = SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Read,
                                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-15),
                                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(120)
                            };

                            var targetContainerToken = targetContainer.GetSharedAccessSignature(policy);
                            var targetContainerSAS = string.Format("{0}{1}", targetContainer.Uri, targetContainerToken);
                            Console.WriteLine($"targetContainerSAS={targetContainerSAS}");

                            TrainingRequestMessage trainingRequestMessage = new TrainingRequestMessage
                            {
                                BlobFolderName = blobContainerFolder,
                                BlobSasUrl = targetContainerSAS,
                                DocumentFormat = documentFormat,
                                IncludeSubFolders = "false",
                                UseLabelFile = "true"
                            };

                            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

                            // Retrieve a reference to a container.
                            CloudQueue queue = queueClient.GetQueueReference(trainingQueueName);

                            // Create the queue if it doesn't already exist
                            await queue.CreateIfNotExistsAsync();

                            CloudQueueMessage message = new CloudQueueMessage(JsonConvert.SerializeObject(trainingRequestMessage));
                            await queue.AddMessageAsync(message);
                        }
                        catch (Exception e)
                        {

                            throw;
                        }


                        Console.WriteLine("done.");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        
                    }
                }
                );
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
                return -1;
            }
            return rootCommand.InvokeAsync(args).Result;
       
        }


    }
}
