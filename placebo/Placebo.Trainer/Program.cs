using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Placebo.Functions.Models;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Placebo.Trainer
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
            storageConnectionString = configuration["storageConnectionString"];
            trainingQueueName = configuration["TrainingQueueName"];
            var shortStorageConnectionString = storageConnectionString.Substring(0, 50);
            Console.WriteLine($"Storage connection string = {shortStorageConnectionString}");

            Console.WriteLine($"Training Queue Name = {trainingQueueName}");

            var rootCommand = new RootCommand
            {
                new Option<string>(
                    "--documentFormat",
                    description: "The document format for which the training assets should be uploaded"),
                new Option<string>(
                    "--labellingContainerSasUrl",
                    description: "SAS Token for container that holds the folder with the labelling and training assets"),
                new Option<string>(
                    "--blobContainerFolder",
                    getDefaultValue: () => null,
                    description: "The name of a folder within the blob container where the assets from the labelling tool aare stored"),
            };

            rootCommand.Description = "This command triggers training of a forms recognizer model, based on assets produced by the labelling tool and stored in a folder in blob storage.";
            try
            {
                rootCommand.Handler = CommandHandler.Create<string, string, string>(async (documentFormat, labellingContainerSasUrl, blobContainerFolder) =>
                {
                    try
                    {
                        Console.WriteLine($"The value for --documentFormat is: {documentFormat}");
                        if (string.IsNullOrEmpty(documentFormat))
                        {
                            throw new Exception($"--documentFormat {documentFormat} must be provided");
                        }

                        
                        Console.WriteLine($"The value for --labellingContainerSasUrl is: {labellingContainerSasUrl}");
                        if (string.IsNullOrEmpty(labellingContainerSasUrl))
                        {
                            throw new Exception($"--blobContainer {labellingContainerSasUrl} must be provided");
                        }

                        Console.WriteLine($"The value for --blobContainerFolder is: {blobContainerFolder}");
                        if (string.IsNullOrEmpty(blobContainerFolder))
                        {
                            throw new Exception($"--blobContainerFolder {blobContainerFolder} must be provided");
                        }

                        // Get hold of the storage account
                        CloudStorageAccount storageAccount = null;
                        try
                        {
                            storageAccount = CloudStorageAccount.Parse(storageConnectionString);
                            TrainingRequestMessage trainingRequestMessage = new TrainingRequestMessage
                            {
                                BlobFolderName = blobContainerFolder,
                                BlobSasUrl = labellingContainerSasUrl,
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
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return -1;
            }
            return rootCommand.InvokeAsync(args).Result;

        }


    }
}
