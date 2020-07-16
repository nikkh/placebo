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
using System.Data;

namespace Placebo.Functions
{
    public class Recognizer
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private RecognizingContext _recognizingContext;
        const string FUNCTION_NAME = "[Recognizer]";
        private readonly string _dbConnectionString;

        public Recognizer(IConfiguration config, TelemetryConfiguration telemetryConfig, RecognizingContext processingContext)
        {
            _config = config;
            _recognizingContext = processingContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");

        }

        [FunctionName("Recognizer")]
        public async Task Run([BlobTrigger("recognize-in-image/{name}", Connection = "IncomingConnection")]Stream incomingImageStream, string name, ILogger log)
        {
            IDictionary<string, string> metadata = new Dictionary<string, string>();
            string thumbprint = "";
            string uniqueRunIdentifier = "";
            Utils utils = new Utils(log, _config["IncomingConnection"], FUNCTION_NAME);
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of blob - Name:{name} Size: {incomingImageStream.Length} Bytes Container: {_recognizingContext.InboundImageContainer}");
            try
            {
                try
                {
                    metadata = await utils.GetBlobMetadataAsync(name, _recognizingContext.InboundImageContainer);
                    if (metadata != null)
                    {
                        if (metadata.ContainsKey(ParsingConstants.UniqueRunIdentifierKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.UniqueRunIdentifierKey]))
                            {
                                uniqueRunIdentifier = metadata[ParsingConstants.UniqueRunIdentifierKey];
                                log.LogDebug($"{FUNCTION_NAME}: Unique Run Identifier was set to {uniqueRunIdentifier}");
                            }
                        }
                        if (metadata.ContainsKey(ParsingConstants.ThumbprintKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.ThumbprintKey]))
                            {
                                thumbprint = metadata[ParsingConstants.ThumbprintKey];
                                log.LogDebug($"{FUNCTION_NAME}: thumbprint is {thumbprint}");
                            }
                        }

                    }
                }
                catch (Exception e)
                {
                    log.LogWarning($"{FUNCTION_NAME}: Unable to set telemetry operation id.  Exception Type: {e.GetType()} Message: {e.Message}");
                }
                

                var recognizerName = new RecognizerName(name);
                
                var model = GetModelIdByDocumentFormat(recognizerName.InvoiceFormat);
              
                log.LogInformation($"{FUNCTION_NAME} Recognition Model {model.ModelId} was selected based on DocumentFormat {recognizerName.InvoiceFormat}");
                log.LogInformation($"{FUNCTION_NAME} Image Type is {recognizerName.ImageType}");
                string contentType = "";
                switch (recognizerName.ImageType)
                {
                    case "jpg":
                        contentType = "image/jpeg";
                        break;
                    case "jpeg":
                        contentType = "image/jpeg";
                        break;
                    case "png":
                        contentType = "image/png";
                        break;
                    case "tiff":
                        contentType = "image/tiff";
                        break;
                    default:
                        {
                            Exception e = new Exception($"{FUNCTION_NAME} Incoming document is {name} is in an unsupported format {recognizerName.ImageType}. Supported type are jpeg, jpg, tiff and png");
                            throw e;
                        }
                }
                string _apiKey = _config["RecognizerApiKey"];
                string _baseUrl = _recognizingContext.RecognizerServiceBaseUrl;

                var queryString = HttpUtility.ParseQueryString(string.Empty);
                queryString["includeTextDetails"] = "True";
                var uri = $"{_baseUrl}{ParsingConstants.FormRecognizerApiPath}/{model.ModelId}/{ParsingConstants.FormRecognizerAnalyzeVerb}?{queryString}";
                HttpResponseMessage response;
                byte[] image = null;
                using (var memoryStream = new MemoryStream())
                {
                    incomingImageStream.CopyTo(memoryStream);
                    image = memoryStream.ToArray();
                }
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
                    using (var postContent = new ByteArrayContent(image))
                    {
                        postContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                        response = await client.PostAsync(uri, postContent);
                    }
                }

                string getUrl = "";
                if (response.IsSuccessStatusCode)
                {
                    HttpHeaders headers = response.Headers;
                    if (headers.TryGetValues("operation-location", out IEnumerable<string> values))
                    {
                        getUrl = values.First();
                    }
                }
                else
                {
                    throw new Exception($"That didnt work.  Trying to submit image for analysis {uri} Content:{response.Content.ReadAsStringAsync().Result}");
                }

                // A job has been submitted, now we need to track the status of it.

                string responseBody = null;
                JObject jsonContent = null;
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
                    for (int i = 0; i < _recognizingContext.MaxRetries; i++)
                    {
                        response = await client.GetAsync(getUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            responseBody = response.Content.ReadAsStringAsync().Result;
                            jsonContent = JObject.Parse(responseBody);
                            if (jsonContent["status"] != null)
                            {
                                string status = jsonContent["status"].ToString();
                                if (status == "succeeded")
                                {
                                    log.LogInformation($"{FUNCTION_NAME} Recognition of image {name} completed sucessfully");
                                    break;
                                }
                                if (status == "failed")
                                {
                                    throw new Exception($"{FUNCTION_NAME} Recognition of image {name} failed. The response body was {responseBody}");
                                }
                                if ((status == "notStarted") || (status == "running"))
                                {
                                    if (i < _recognizingContext.MaxRetries)
                                    {
                                        int waitDuration = (i + 1) * 3;
                                        log.LogDebug($"{FUNCTION_NAME} Submitted analysis attempt {i}.  Status is {status}. will sleep for {waitDuration} seconds and retry");
                                        Thread.Sleep(1000 * waitDuration);
                                        continue;
                                    }
                                    else
                                    {
                                        throw new Exception($"{FUNCTION_NAME} Recognizer did not complete in the allotted time and was abandoned.  Value is {status}");
                                    }
                                }
                                throw new Exception($"{FUNCTION_NAME} Hmmmmmnn?  Checking recognizer progress.  status element is not null, but also not one of the documented values.  Value is {status}");
                            }
                            else
                            {
                                throw new Exception($"{FUNCTION_NAME} Hmmmmmnn?  Checking analysis progress.  Get request was sucessful, but status element is null?");
                            }
                        }
                        else
                        {
                            throw new Exception($"{FUNCTION_NAME} That didnt work.  Trying to submit image for analysis {uri} Response:{response.StatusCode.ToString()}");
                        }
                    }
                }

                // if we get here then we have our results (in JsonContent)
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.ThumbprintKey, thumbprint);
                metadata.Add(ParsingConstants.ModelIdKey, model.ModelId);
                metadata.Add(ParsingConstants.ModelVersionKey, model.ModelVersion.ToString());
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, uniqueRunIdentifier);
                await utils.SaveBlobAsync(_recognizingContext.OutboundJsonContainer, $"{name}{ParsingConstants.RecognizedExtension}", jsonContent.ToString(), metadata);
                await utils.MoveBlobAsync(_recognizingContext.InboundImageContainer, _recognizingContext.ProcessingCompleteContainer, name);
            }
            // unexpected failure 
            catch (Exception e)
            {
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, uniqueRunIdentifier);
                _telemetryClient.TrackException(e);
                log.LogError($"{FUNCTION_NAME} Unexpected error.  Exception Type: {e.GetType().ToString()} Message {e.Message}");
                await utils.MoveBlobAsync(_recognizingContext.InboundImageContainer, _recognizingContext.ExceptionContainer, name);
                await utils.SaveBlobAsync(_recognizingContext.ExceptionContainer, name + ParsingConstants.ExceptionExtension, e.ToString(), metadata);
                throw;
            }
        }

        private DocumentFormatModel GetModelIdByDocumentFormat(string documentFormat)
        {
            var documentFormatModel = new DocumentFormatModel();
            using (SqlConnection connection = new SqlConnection(_dbConnectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "GetModelByDocumentFormat";
                command.Parameters.Add(new SqlParameter("@DocumentFormat", documentFormat));
                command.Connection = connection;

                try
                {

                    SqlDataReader reader = command.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            documentFormatModel.DocumentFormat = Convert.ToString(reader["DocumentFormat"]);
                            documentFormatModel.ModelId = Convert.ToString(reader["ModelId"]);
                            documentFormatModel.ModelVersion = Convert.ToInt32(reader["ModelVersion"]);
                            documentFormatModel.UpdatedDateTime = Convert.ToDateTime(reader["UpdatedDateTime"]);
                            documentFormatModel.AverageModelAccuracy = Convert.ToDecimal(reader["AverageModelAccuracy"]);


                        }

                    }
                    finally
                    {
                        reader.Close();
                    }




                }
                finally { }
                return documentFormatModel;
            }
        }
    }
}

