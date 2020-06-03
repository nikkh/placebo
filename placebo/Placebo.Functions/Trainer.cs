using System;
using System.IO;
using System.Text.Json;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Placebo.Functions.Models;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Placebo.Functions.Contexts;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Text;
using System.Collections.Generic;
using System.Threading;

namespace Placebo.Functions
{
    public class Trainer
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private readonly TrainingContext _trainingContext;
        const string FUNCTION_NAME = "[Trainer]";
        private static HttpClient _httpClient = new HttpClient();
        private readonly string _dbConnectionString;

        public Trainer(IConfiguration config, TelemetryConfiguration telemetryConfig, TrainingContext trainingContext)
        {
            _config = config;
            _trainingContext = trainingContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");

        }

        [FunctionName("Trainer")]
        public async Task Run([QueueTrigger("training", Connection = "IncomingConnection")]string message, ILogger log)
        {
           
            Utils utils = new Utils(log, _config["IncomingConnection"], FUNCTION_NAME);
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of message  - Message:{message}");
            try
            {
                TrainingRequestMessage trm = JsonConvert.DeserializeObject<TrainingRequestMessage>(message);
                string _apiKey = _config["RecognizerApiKey"];
                string _baseUrl = _trainingContext.RecognizerServiceBaseUrl;
                

                var uri = $"{_baseUrl}{ParsingConstants.FormRecognizerApiPath}";
               
                JObject body = new JObject(
                    new JProperty("source", trm.BlobSasUrl),
                    new JProperty("sourceFilter", 
                        new JObject(
                            new JProperty("prefix", trm.BlobFolderName), 
                            new JProperty("includeSubFolders", trm.IncludeSubFolders)
                        )
                    ),
                    new JProperty("useLabelFile", trm.UseLabelFile)
                );
                string json = body.ToString();

                string getUrl = "";
                using (var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                {
                    _httpClient.DefaultRequestHeaders.Add(ParsingConstants.OcpApimSubscriptionKey, _apiKey);
                    HttpResponseMessage response = await _httpClient.PostAsync(uri, content);
                    if (response.IsSuccessStatusCode)
                    {
                        HttpHeaders headers = response.Headers;
                        if (headers.TryGetValues("location", out IEnumerable<string> values))
                        {
                            getUrl = values.First();
                            log.LogInformation($"{FUNCTION_NAME} Model training request accepted by Forms Recognizer {_baseUrl}");
                        }
                    }
                    else
                    {
                        var test = await response.Content.ReadAsStringAsync();
                        throw new Exception($"That didnt work.  Trying to submit model training request {test} request was {json} Response:{response.StatusCode.ToString()}");
                    }
                }

                // TODO This needs to be in a 'monitor pattern' durable function
                // A job has been submitted, now we need to track the status of it.

                string responseBody = null;
                JObject jsonContent = null;
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
                for (int i = 0; i < _trainingContext.MaxRetries; i++)
                {
                    HttpResponseMessage response = await _httpClient.GetAsync(getUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        responseBody = response.Content.ReadAsStringAsync().Result;
                        jsonContent = JObject.Parse(responseBody);
                        if (jsonContent["modelInfo"]["status"] != null)
                        {
                            string status = jsonContent["modelInfo"]["status"].ToString();
                            if (status == "ready")
                            {
                                log.LogInformation($"{FUNCTION_NAME} Training completed sucessfully");
                                trm.ModelId = jsonContent["modelInfo"]["modelId"].ToString();
                                
                                
                                string dateAsString = DateTime.Now.ToString();
                                try
                                {
                                    dateAsString = jsonContent["modelInfo"]["createdDateTime"].ToString();
                                }
                                catch{}

                                DateTime dateValue;
                                if (DateTime.TryParse(dateAsString, out dateValue)) trm.CreatedDateTime = dateValue;

                                dateAsString = DateTime.Now.ToString();
                                try
                                {
                                    dateAsString = jsonContent["modelInfo"]["lastUpdatedDateTime"].ToString();
                                }
                                catch { }

                                if (DateTime.TryParse(dateAsString, out dateValue)) trm.UpdatedDateTime = dateValue;



                                string numberAsString = "0";
                                try
                                {
                                    numberAsString = jsonContent["trainResult"]["averageModelAccuracy"].ToString();
                                }
                                catch { }

                                decimal numberValue = 0;
                                if (Decimal.TryParse(numberAsString, out numberValue))
                                {
                                    trm.AverageModelAccuracy = numberValue;
                                }
                                else { trm.AverageModelAccuracy = 0; }

                                trm.TrainingDocumentResults = jsonContent["trainResult"]["trainingDocuments"].ToString();
                                break;
                            }
                            if (status == "invalid")
                            {
                                throw new Exception($"{FUNCTION_NAME} Training failed. The response body was {responseBody}");
                            }
                            
                            if (i < _trainingContext.MaxRetries)
                            {
                                int waitDuration = (i + 1) * 3;
                                log.LogDebug($"{FUNCTION_NAME} Training attempt {i}.  Status is {status}. will sleep for {waitDuration} seconds and retry");
                                Thread.Sleep(1000 * waitDuration);
                                continue;
                            }
                            else
                            {
                                throw new Exception($"{FUNCTION_NAME} Training did not complete in the allotted time and was abandoned.  Value is {status}");
                            }
                           
                        }
                        else
                        {
                            throw new Exception($"{FUNCTION_NAME} Hmmmmmnn?  Training model progress check.  Get request was sucessful, but status element is null?");
                        }
                        
                    }
                    else
                    {
                        throw new Exception($"{FUNCTION_NAME} Training the model failed {uri} Response:{response.StatusCode.ToString()}");
                    }
                }

                // get here we are ready to go...
                log.LogInformation($"Model for invoice format {trm.DocumentFormat} was trained sucessfully.  Model Id is {trm.ModelId}. Average model accuracy={trm.AverageModelAccuracy}");
                UpdateModelTraining(trm, log);
            }
            // unexpected failure 
            catch (Exception e)
            {
              
                _telemetryClient.TrackException(e);
                log.LogError($"{FUNCTION_NAME} Unexpected error.  Exception Type: {e.GetType().ToString()} Message {e.Message}");
                throw;
            }
        }
        #region Database Methods 
        private void UpdateModelTraining(TrainingRequestMessage m, ILogger log)
        {
            using (SqlConnection connection = new SqlConnection(_dbConnectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                SqlTransaction transaction;
                transaction = connection.BeginTransaction("TrainingRequestTransaction");
                command.Connection = connection;
                command.Transaction = transaction;
                int currentVersion = 0;
                int newVersion = 0;
                try
                {
                    
                    command.CommandText = $"SELECT MAX(ModelVersion) AS Current_Version from ModelTraining WHERE DocumentFormat='{m.DocumentFormat}'";
                    SqlDataReader reader = command.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            if (reader["Current_Version"] != System.DBNull.Value)
                            {
                                currentVersion = Convert.ToInt32(reader["Current_Version"]);
                            }
                            else
                            {
                                currentVersion = 0;
                            }
                        }

                    }
                    finally
                    {
                        reader.Close();
                    }
                    
                    newVersion = currentVersion + 1;

                    // Add the row 
                    string insertClause = $"Insert into ModelTraining (DocumentFormat, ModelVersion, ModelId, CreatedDateTime, UpdatedDateTime, BlobSasUrl, BlobfolderName, IncludeSubFolders, UseLabelFile, AverageModelAccuracy, TrainingDocumentResults";
                    string valuesClause = $" VALUES ('{m.DocumentFormat}', '{newVersion}','{m.ModelId}', '{m.CreatedDateTime:yyyy-MM-dd HH:mm:ss.fff}', '{m.UpdatedDateTime:yyyy-MM-dd HH:mm:ss.fff}', '{m.BlobSasUrl}', '{m.BlobFolderName}','{m.IncludeSubFolders}', '{m.UseLabelFile}','{m.AverageModelAccuracy}','{m.TrainingDocumentResults}'";
                    insertClause += ") ";
                    valuesClause += ")";
                    command.CommandText = insertClause + valuesClause;


                    command.ExecuteNonQuery();
                   

                    transaction.Commit();
                }
                catch (Exception e)
                {
                    log.LogError($"Exception prevented writing training request for document format {m.DocumentFormat} model id={m.ModelId} to database (transaction was rolled back).  Message is {e.Message}");
                    transaction.Rollback();
                    throw e;
                }

                log.LogInformation($"{FUNCTION_NAME} Training request for document format {m.DocumentFormat}, version={newVersion}, model id={m.ModelId}  was written to the database");

            }
        }
        #endregion
    }
}

