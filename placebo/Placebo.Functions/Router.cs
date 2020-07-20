using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Placebo.Functions.Contexts;
using Placebo.Functions.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;

namespace Placebo.Functions
{
    public class Router
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _dbConnectionString;
        private RoutingContext _routingContext;
        const string FUNCTION_NAME = "[Router]";

        public Router(IConfiguration config, TelemetryConfiguration telemetryConfig, RoutingContext routingContext)
        {
            _config = config;
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");
            _routingContext = routingContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);
           

        }

        [FunctionName("Router")]
        public async Task Run([QueueTrigger("incoming", Connection = "PlaceboStaging")]string message, ILogger log)
        {
            var operationId = _telemetryClient.Context.Operation.Id;
            var parentId = _telemetryClient.Context.Operation.ParentId;
            string uniqueRunIdentifier = Guid.NewGuid().ToString();
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of message  - Message:{message}");
            log.LogDebug($"OperationId={operationId}");
            log.LogDebug($"ParentId={parentId}");
            var utils = new Utils(log, FUNCTION_NAME, _config);
            utils.CheckAndCreateDatabaseIfNecessary();
            try
            {

                
                JObject jsonContent = JObject.Parse(message);
                var url = jsonContent["data"]["url"].ToString();
                var uri = new Uri(url);
                string[] parts = uri.Segments; 
                Console.WriteLine("Segments");
                string incomingStorageContainer = null;
                string invoiceFormat = null;
                foreach (var part in parts)
                {
                    if (part.Contains("drop-"))
                    {
                        incomingStorageContainer = part.Substring(0, (part.Length - 1));
                        var IndexFirstCharacterAfterHyphen = part.LastIndexOf('-') + 1;
                        var lengthOfInvoiceFormat = incomingStorageContainer.Length - IndexFirstCharacterAfterHyphen;
                        invoiceFormat = incomingStorageContainer.Substring(IndexFirstCharacterAfterHyphen, lengthOfInvoiceFormat);
                        break;
                    }
                }

                string sourceBlobName = Uri.UnescapeDataString(parts[parts.GetUpperBound(0)]);
                string targetBlobName = $"{invoiceFormat}-{sourceBlobName}";

                log.LogDebug($"{FUNCTION_NAME} Incoming storage container is {incomingStorageContainer}");
                log.LogDebug($"{FUNCTION_NAME} Incoming invoice format is {invoiceFormat}");
                log.LogDebug($"{FUNCTION_NAME} Incoming blob url is  {url}");
                log.LogDebug($"{FUNCTION_NAME} SourceBlobName is {sourceBlobName}");
                log.LogDebug($"{FUNCTION_NAME} TargetBlobName is {targetBlobName}");

                utils = new Utils(log, _config["IncomingConnection"], FUNCTION_NAME, _config["PlaceboStaging"] );
                string thumbprint = await utils.GetBlobMD5(sourceBlobName, incomingStorageContainer);
                Dictionary<string, string> metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.ThumbprintKey, thumbprint);
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, uniqueRunIdentifier);
                if (string.IsNullOrEmpty(operationId) && (string.IsNullOrEmpty(parentId)))
                {
                    metadata.Add(ParsingConstants.TelemetryOperationIdKey, operationId);
                    metadata.Add(ParsingConstants.TelemetryOperationParentIdKey, parentId);
                }
               
                await utils.MoveRenameBlobAsync(incomingStorageContainer, sourceBlobName, _routingContext.TargetContainer, targetBlobName, metadata);

            }
            catch (Exception e)
            {
                _telemetryClient.TrackException(e);
                log.LogError($"{FUNCTION_NAME} Unexpected error. Exception Type: {e.GetType().ToString()} Message {e.Message}.  Please refer to application insights for diagnosis");
                throw;
            }
        }

    }
}
