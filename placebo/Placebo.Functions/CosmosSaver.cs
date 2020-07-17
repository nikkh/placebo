using System;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Placebo.Functions.Contexts;
using Placebo.Functions.Models;

namespace Placebo.Functions
{
    public class CosmosSaver
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _databaseId;
        private readonly string _containerId;
        private readonly CosmosClient _cosmosClient;
        private readonly string  _cosmosAuthorizationKey;
        private readonly string _cosmosEndPointUrl;
        private CosmosContext _persistingContext;
        const string FUNCTION_NAME = "[CosmosSaver]";

        public CosmosSaver(IConfiguration config, TelemetryConfiguration telemetryConfig, CosmosContext cosmosContext)
        {
            _config = config;
            _persistingContext = cosmosContext;
            _cosmosAuthorizationKey = _config["CosmosAuthorizationKey"];
            _telemetryClient = new TelemetryClient(telemetryConfig);
            _cosmosEndPointUrl = _config["CosmosEndPointUrl"];
            if (_cosmosClient == null)
            {
                string endpoint = _cosmosEndPointUrl;
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new ArgumentNullException("Please specify a valid endpoint in the appSettings.json");
                }

                string authKey = _cosmosAuthorizationKey;
                if (string.IsNullOrEmpty(authKey) || string.Equals(authKey, "Super secret key"))
                {
                    throw new ArgumentException("Please specify a valid AuthorizationKey in the appSettings.json");
                }

                _databaseId = _persistingContext.CosmosDatabaseId;
                if (string.IsNullOrEmpty(_databaseId))
                {
                    throw new ArgumentException("Please specify a valid Cosmos Database Id in the settings.json");
                }

                _containerId = _persistingContext.CosmosContainerId;
                if (string.IsNullOrEmpty(_containerId))
                {
                    throw new ArgumentException("Please specify a valid Cosmos Database Container Id in the settings.json");
                }
                _cosmosClient = new CosmosClient(endpoint, authKey);
            }
        }

        [FunctionName("CosmosPersistor")]
        public async Task Run([ServiceBusTrigger("to-cosmos", Connection = "ServiceBusConnectionString")] Message message, ILogger log)
        {
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of service bus message");
            string payload = System.Text.Encoding.UTF8.GetString(message.Body);
            var messageModel = JsonConvert.DeserializeObject<MessageModel>(payload);
            var document = JsonConvert.DeserializeObject<Document>(messageModel.Payload);
           
                Database database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseId);
                ContainerProperties containerProperties = new ContainerProperties(_containerId, partitionKeyPath: "/Account");
                Container container = await database.CreateContainerIfNotExistsAsync(
                    containerProperties,
                    throughput: 400);
                _ = await container.CreateItemAsync(document, new PartitionKey(document.Account),
                new ItemRequestOptions()
                {
                    EnableContentResponseOnWrite = false
                });
                log.LogInformation($"{FUNCTION_NAME} - document {document.DocumentNumber} was saved to Cosmos - database={_databaseId}, container={_containerId})");
            
            
        }
    }
}
