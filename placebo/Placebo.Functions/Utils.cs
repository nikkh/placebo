using Placebo.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Placebo.Functions
{
    public class Utils
    {
        private readonly ILogger _log;
        private readonly string _mainStorageConnectionString;
        private readonly string _functionName;
        private string _stagingStorageConnectionString;

        public string StagingStorageConnectionString { get => _stagingStorageConnectionString; set => _stagingStorageConnectionString = value; }

        private Utils() { }
        public Utils(ILogger log, string mainStorageAccountConnectionString, string functionName, string stagingStorageAccountConnectionString = null)
        {
            _log = log;
            _functionName = functionName;
            _mainStorageConnectionString = mainStorageAccountConnectionString;
            StagingStorageConnectionString = stagingStorageAccountConnectionString;
        }

        #region blob handling methods
        private CloudStorageAccount GetStorageAccount(string connectionString)
        {
            // Get a reference to the storage account
            CloudStorageAccount storageAccount = null;
            try
            {
                storageAccount = CloudStorageAccount.Parse(connectionString);
                return storageAccount;
            }
            catch (Exception e)
            {
                _log.LogError($"{_functionName} Unable access storage account, check connection string {connectionString}");
                throw;
            }
        }
        public  async Task MoveRenameBlobAsync(string sourceContainerName, string sourceBlobName, string targetContainerName, string targetBlobName, Dictionary<string, string> metadata = null)
        {
            string methodName = "CopyRenameBlobAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Copying {sourceContainerName}/{sourceBlobName} to {targetContainerName}/{targetBlobName}");

            if (StagingStorageConnectionString == null)
            {
                throw new Exception($"{_loggingPrefix}: stagingStorageConnectionString must be set before calling this method");
            }

            string sourceConnectionString = StagingStorageConnectionString;
            CloudStorageAccount sourceStorageAccount = GetStorageAccount(sourceConnectionString);
            CloudStorageAccount targetStorageAccount = GetStorageAccount(_mainStorageConnectionString);
            // Get a reference to the archive container
            CloudBlobClient sourceBlobClient;
            CloudBlobContainer sourceContainer;
            CloudBlobClient targetBlobClient;
            CloudBlobContainer targetContainer;
            try
            {
                var policy = new SharedAccessBlobPolicy
                {
                    Permissions = SharedAccessBlobPermissions.Read,
                    SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-15),
                    SharedAccessExpiryTime = DateTime.UtcNow.AddDays(7)
                };

                sourceBlobClient = sourceStorageAccount.CreateCloudBlobClient();
                sourceContainer = sourceBlobClient.GetContainerReference(sourceContainerName);
                var sourceBlob = sourceContainer.GetBlockBlobReference(sourceBlobName);
                await sourceBlob.FetchAttributesAsync();
                var sourceBlobToken = sourceBlob.GetSharedAccessSignature(policy);
                var sourceBlobSAS = string.Format("{0}{1}", sourceBlob.Uri, sourceBlobToken);

                targetBlobClient = targetStorageAccount.CreateCloudBlobClient();
                targetContainer = targetBlobClient.GetContainerReference(targetContainerName);
                var targetBlob = targetContainer.GetBlockBlobReference(targetBlobName);

                if (metadata != null)
                {
                    targetBlob = await SetBlobMetadataAsync(targetBlob, metadata);
                }

                _log.LogDebug($"{_loggingPrefix}: Copying {sourceContainerName}/{sourceBlobName} to {targetContainerName}/{targetBlobName} has started");
                await targetBlob.StartCopyAsync(new Uri(sourceBlobSAS)).ConfigureAwait(false);

                while (true)
                {
                    await targetBlob.FetchAttributesAsync();
                    if  (targetBlob.CopyState.Status == CopyStatus.Pending)
                    {
                        _log.LogDebug($"{_loggingPrefix}: Copy is still in-progress.  Will wait 3 seconds and try again...");
                        Thread.Sleep(3000);
                        continue;
                    }
                    if (targetBlob.CopyState.Status == CopyStatus.Success)
                    {
                        _log.LogDebug($"{_loggingPrefix}: Copy succeeded");
                        break;
                    }
                    throw new Exception($"{_loggingPrefix}: Copy { sourceBlob.Name } to { targetBlob.Name} did not complete.  CopyState={targetBlob.CopyState.Status}...");
                }

                await sourceBlob.DeleteAsync();
                _log.LogDebug($"{_loggingPrefix}: Deleted {sourceBlob.Name}");
                _log.LogInformation($"{_loggingPrefix}: {sourceBlobName} renamed to {targetBlobName} and moved from {sourceContainerName} to container {targetContainerName}");
            }
            catch (Exception e)
            {
                _log.LogDebug($"{_loggingPrefix}: sourceContainer={sourceContainerName}");
                _log.LogDebug($"{_loggingPrefix}:document={sourceBlobName}");
                _log.LogDebug($"{_loggingPrefix}:targetContainer={targetContainerName}");
                _log.LogError($"{_loggingPrefix}: Error renaming blob {sourceBlobName} to {targetBlobName} and moving from {sourceContainerName} to container {targetContainerName}.  Exception Message: {e.Message}).");
                throw;
            }
        }

        internal async Task<IDictionary<string, string>> GetBlobMetadataAsync(string blobName, string containerName)
        {
            string methodName = "GetBlobMetadataAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Retrieving metadata from {containerName}/{blobName}");

            CloudStorageAccount storageAccount = GetStorageAccount(_mainStorageConnectionString);
            CloudBlobClient blobClient;
            CloudBlobContainer container;
            CloudBlockBlob blob=null;
            try
            {
                blobClient = storageAccount.CreateCloudBlobClient();
                container = blobClient.GetContainerReference(containerName);
                blob = container.GetBlockBlobReference(blobName);
                await blob.FetchAttributesAsync();
               
            }
            catch (Exception e)
            {
                _log.LogWarning($"{_loggingPrefix}: Unable to retrieving metadata from {containerName}/{blobName}");
            }
            return blob.Metadata;
        }

        private async Task<CloudBlockBlob> SetBlobMetadataAsync(CloudBlockBlob blob, IDictionary<string, string> metadata)
        {
            string methodName = "SetBlobMetadata";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Setting metadata for blob {blob.Name}");
            try { 
                foreach (var item in metadata)
                {
                    if (!String.IsNullOrEmpty(item.Value)) 
                    {
                        _log.LogDebug($"{_loggingPrefix}: Metadata: {item.Key}={item.Value}");
                        blob.Metadata[item.Key] = item.Value;
                    }
                }
                return blob;
            }
            catch (Exception e)
            {
                _log.LogError($"{_loggingPrefix}: Setting metadata for blob {blob.Name}. Exception Message: {e.Message})");
                throw;
            }
        }

        public async Task SaveBlobAsync(string targetContainerName, string targetBlobName, string blobContents, IDictionary<string, string> metadata = null)
        {
            string methodName = "SaveBlobAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Saving blob {targetContainerName}/{targetBlobName}");
            CloudStorageAccount storageAccount = GetStorageAccount(_mainStorageConnectionString);
            CloudBlobClient blobClient;
            CloudBlobContainer targetContainer;
            try
            {
                blobClient = storageAccount.CreateCloudBlobClient();
                targetContainer = blobClient.GetContainerReference(targetContainerName);
                var outputBlob = targetContainer.GetBlockBlobReference(targetBlobName);
                if (metadata != null)
                {
                    outputBlob = await SetBlobMetadataAsync(outputBlob, metadata);
                }
                await outputBlob.UploadTextAsync(blobContents);
                _log.LogInformation($"{_loggingPrefix}: Blob {targetBlobName} was saved (in Json format) to container {targetContainerName}");
            }
            catch (Exception e)
            {
                _log.LogDebug($"{_loggingPrefix} targetContainer={targetContainerName}");
                _log.LogDebug($"{_loggingPrefix} targetBlob={targetBlobName}");
                _log.LogError($"{_loggingPrefix} Error saving blob {targetBlobName} to container {targetContainerName}.  Exception Message: {e.Message}).");
                throw;
            }
        }

        public async Task MoveBlobAsync(string sourceContainerName, string targetContainerName, string blobName, IDictionary<string, string> metadata = null)
        {
            string methodName = "MoveBlobAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Moving blob {blobName} from {sourceContainerName} to {targetContainerName}");
            CloudStorageAccount storageAccount = GetStorageAccount(_mainStorageConnectionString);
            // Get a reference to the archive container
            CloudBlobClient blobClient;
            CloudBlobContainer sourceContainer;
            CloudBlobContainer targetContainer;
            try
            {
                blobClient = storageAccount.CreateCloudBlobClient();
                sourceContainer = blobClient.GetContainerReference(sourceContainerName);
                var sourceBlob = sourceContainer.GetBlockBlobReference(blobName);
                targetContainer = blobClient.GetContainerReference(targetContainerName);
                var targetBlob = targetContainer.GetBlockBlobReference(blobName);
                if (metadata != null)
                {
                    targetBlob = await SetBlobMetadataAsync(targetBlob, metadata);
                }
                await targetBlob.StartCopyAsync(sourceBlob);

                while (true)
                {
                    await targetBlob.FetchAttributesAsync();
                    if (targetBlob.CopyState.Status == CopyStatus.Pending)
                    {
                        _log.LogDebug($"{_loggingPrefix} Copying {sourceBlob.Name} to {targetBlob.Name} is still in-progress.  Will wait 3 seconds and try again...");
                        Thread.Sleep(3000);
                        continue;
                    }
                    if (targetBlob.CopyState.Status == CopyStatus.Success)
                    {
                        _log.LogDebug($"{_loggingPrefix} Copying {sourceBlob.Name} to {targetBlob.Name} succeeded");
                        break;
                    }
                    throw new Exception($"{_loggingPrefix} Copying { sourceBlob.Name } to { targetBlob.Name} did not complete.  CopyState={targetBlob.CopyState.Status}...");
                }

                await sourceBlob.DeleteAsync();
                _log.LogInformation($"{_loggingPrefix} Document {blobName} was moved from {sourceContainerName} to container {targetContainerName}");
            }
            catch (Exception e)
            {
                _log.LogDebug($"{_loggingPrefix} sourceContainer={sourceContainerName}");
                _log.LogDebug($"{_loggingPrefix} document={blobName}");
                _log.LogDebug($"{_loggingPrefix} targetContainer={targetContainerName}");
                _log.LogError($"{_loggingPrefix} Error moving blob {blobName} from {sourceContainerName} to container {targetContainerName}.  Exception Message: {e.Message}).");
                throw;
            }
        }

        public async Task SaveInvoiceAsync(Invoice invoice, string containerName, string blobName, IDictionary<string, string> metadata = null)
        {
            string methodName = "SaveInvoiceAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Saving Invoice {containerName}/{blobName}");

            CloudStorageAccount storageAccount = GetStorageAccount(_mainStorageConnectionString);
            CloudBlobClient blobClient;
            CloudBlobContainer targetContainer;
            try
            {
                blobClient = storageAccount.CreateCloudBlobClient();
                targetContainer = blobClient.GetContainerReference(containerName);
                var outputBlob = targetContainer.GetBlockBlobReference(blobName);
                if (metadata != null)
                {
                    outputBlob = await SetBlobMetadataAsync(outputBlob, metadata);
                }
                await outputBlob.UploadTextAsync(invoice.ToJsonString());
                _log.LogInformation($"{_loggingPrefix} The invoice produced was serialized and saved (in Json format) to {blobName} in container {containerName}");
            }
            catch (Exception e)
            {
                _log.LogDebug($"{_loggingPrefix} targetContainer={containerName}");
                _log.LogDebug($"{_loggingPrefix} inputBlobName={blobName}");
                _log.LogError($"{_loggingPrefix} Error saving serialized invoice output to container {containerName}.  Exception Message: {e.Message}). Function will terminate");
                throw;
            }

        }

        public async Task SaveDocumentAsync(Document document, string containerName, string blobName, IDictionary<string, string> metadata = null)
        {
            string methodName = "SaveDocumentAsync";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: Saving Document {containerName}/{blobName}");

            CloudStorageAccount storageAccount = GetStorageAccount(_mainStorageConnectionString);
            CloudBlobClient blobClient;
            CloudBlobContainer targetContainer;
            try
            {
                blobClient = storageAccount.CreateCloudBlobClient();
                targetContainer = blobClient.GetContainerReference(containerName);
                var outputBlob = targetContainer.GetBlockBlobReference(blobName);
                if (metadata != null)
                {
                    outputBlob = await SetBlobMetadataAsync(outputBlob, metadata);
                }
                await outputBlob.UploadTextAsync(document.ToJsonString());
                _log.LogInformation($"{_loggingPrefix} The document produced was serialized and saved (in Json format) to {blobName} in container {containerName}");
            }
            catch (Exception e)
            {
                _log.LogDebug($"{_loggingPrefix} targetContainer={containerName}");
                _log.LogDebug($"{_loggingPrefix} inputBlobName={blobName}");
                _log.LogError($"{_loggingPrefix} Error saving serialized document output to container {containerName}.  Exception Message: {e.Message}). Function will terminate");
                throw;
            }

        }

        internal async Task<string> GetBlobMD5(string blobName, string containerName)
        {
            string methodName = "GetBlobMD5";
            string _loggingPrefix = $"{_functionName}-{ methodName}";
            _log.LogDebug($"{_loggingPrefix}: computing MD5 hash for {containerName}/{blobName}");

            if (StagingStorageConnectionString == null)
            {
                throw new Exception($"{_loggingPrefix}: stagingStorageConnectionString must be set before calling this method");
            }

            string sourceConnectionString = StagingStorageConnectionString;
            CloudStorageAccount sourceStorageAccount = GetStorageAccount(sourceConnectionString);
            CloudBlobClient blobClient;
            CloudBlobContainer container;
            CloudBlockBlob blob = null;
            byte[] md5hash = null;
            try
            {
                blobClient = sourceStorageAccount.CreateCloudBlobClient();
                container = blobClient.GetContainerReference(containerName);
                blob = container.GetBlockBlobReference(blobName);
                var memoryStream = new MemoryStream();
                await blob.DownloadToStreamAsync(memoryStream);
                using (var md5 = MD5.Create())
                {
                    using (memoryStream)
                    {
                        memoryStream.Position = 0;
                        md5hash = md5.ComputeHash(memoryStream);
                    }
                }
                
            }
            catch (Exception e)
            {
                _log.LogWarning($"{_loggingPrefix}: Unable to retrieving metadata from {containerName}/{blobName}");
            }
            return BitConverter.ToString(md5hash).Replace("-", " ");
        }

        #endregion

    }
}
