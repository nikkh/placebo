using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Placebo.Functions.Models;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Placebo.Functions.Contexts;
using System.Collections.Generic;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Placebo.Functions
{
    public class Processor
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _dbConnectionString;
        private readonly ServiceBusSender _serviceBusSender;
        private ProcessingContext _processingContext;
        const string FUNCTION_NAME = "[Processor]";
   
        public Processor(IConfiguration config, TelemetryConfiguration telemetryConfig, ProcessingContext processingContext, ServiceBusSender serviceBusSender)
        {
            _config = config;
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");
            _processingContext = processingContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);
            _serviceBusSender = serviceBusSender;
        }

        [FunctionName("Processor")]
        public async Task Run([BlobTrigger("process-in-json/{name}", Connection = "IncomingConnection")]Stream incomingBlobStream, string name,  ILogger log)
        {
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of blob - Name:{name} Size: {incomingBlobStream.Length} Bytes Container: {_processingContext.InboundDocumentContainer}");
            string thumbprint = "";
            string modelId = "";
            string modelVersion = "";
            string operationId = "";
            string parentId = "";
            Utils utils = new Utils(log, _config["IncomingConnection"], FUNCTION_NAME);
            
            IDictionary<string, string> metadata = new Dictionary<string, string>();
            Document document = new Document { FileName = name };
            document.UniqueRunIdentifier = Guid.NewGuid().ToString();
            try
            {
                try
                {
                    metadata = await utils.GetBlobMetadataAsync(name, _processingContext.InboundDocumentContainer);
                    if (metadata != null)
                    {
                        if (metadata.ContainsKey(ParsingConstants.UniqueRunIdentifierKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.UniqueRunIdentifierKey]))
                            {
                                document.UniqueRunIdentifier = metadata[ParsingConstants.UniqueRunIdentifierKey];
                                log.LogDebug($"{FUNCTION_NAME} unique run identifier was set to  {document.UniqueRunIdentifier}");
                            }
                        }
                        if (metadata.ContainsKey(ParsingConstants.ThumbprintKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.ThumbprintKey]))
                            {
                                thumbprint = metadata[ParsingConstants.ThumbprintKey];
                                log.LogDebug($"{FUNCTION_NAME} thumbprint is {thumbprint}");
                            }
                        }
                        if (metadata.ContainsKey(ParsingConstants.ModelIdKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.ModelIdKey]))
                            {
                                modelId = metadata[ParsingConstants.ModelIdKey];
                                log.LogDebug($"{FUNCTION_NAME} modelId is {modelId}");
                            }
                        }
                        if (metadata.ContainsKey(ParsingConstants.ModelVersionKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.ModelVersionKey]))
                            {
                                modelVersion = metadata[ParsingConstants.ModelVersionKey];
                                log.LogDebug($"{FUNCTION_NAME} modelVersion is {modelVersion}");
                            }
                        }
                        if (metadata.ContainsKey(ParsingConstants.TelemetryOperationIdKey) && metadata.ContainsKey(ParsingConstants.TelemetryOperationParentIdKey))
                        {
                            if (!String.IsNullOrEmpty(metadata[ParsingConstants.TelemetryOperationIdKey]) && !String.IsNullOrEmpty(metadata[ParsingConstants.TelemetryOperationIdKey]))
                            {
                                operationId = metadata[ParsingConstants.TelemetryOperationIdKey];
                                parentId = metadata[ParsingConstants.TelemetryOperationParentIdKey];
                                log.LogDebug($"{FUNCTION_NAME} setting Application Insights Telemetry.  OperationId = {operationId}, ParentId = {parentId}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    log.LogWarning($"{FUNCTION_NAME} Error reading incoming blob metadata.  Exception Type: {e.GetType()} Message: {e.Message}");
                }

                _telemetryClient.Context.Operation.Id = operationId;
                _telemetryClient.Context.Operation.ParentId = parentId;
                log.LogInformation("Check the telemetry operation and parent associated with this request - search gooseberry!");
                string content = "";
                using (var sr = new StreamReader(incomingBlobStream))
                {
                    content = sr.ReadToEnd();
                }
                
                Stopwatch timer = new Stopwatch();
                timer.Start();

                JObject jsonContent = JObject.Parse(content);
                if (jsonContent["status"] != null) document.RecognizerStatus = jsonContent["status"].ToString();
                if (jsonContent["errors"] != null) document.RecognizerErrors = jsonContent["errors"].ToString();

                // Fill out the document object
                var nittyGritty = (JObject)jsonContent["analyzeResult"]["documentResults"][0]["fields"];
                log.LogInformation($"{FUNCTION_NAME} Creating document based on forms recognizer output");
                document.ShreddingUtcDateTime = DateTime.Now;
                document.OrderNumber = GetString(ParsingConstants.OrderNumber, nittyGritty, document);
                document.OrderDate = GetDate(ParsingConstants.OrderDate, nittyGritty, document);
                document.TaxDate = GetDate(ParsingConstants.TaxDate, nittyGritty, document);
                document.DocumentNumber = GetString(ParsingConstants.InvoiceNumber, nittyGritty, document);
                document.Account = GetString(ParsingConstants.Account, nittyGritty, document);
                document.NetTotal = GetNumber(ParsingConstants.NetTotal, nittyGritty, document) ?? 0;
                document.VatAmount = GetNumber(ParsingConstants.VatAmount, nittyGritty, document) ?? 0;
                document.GrandTotal = GetNumber(ParsingConstants.GrandTotal, nittyGritty, document) ?? 0;
                document.PostCode = GetString(ParsingConstants.PostCode, nittyGritty, document);
                document.TimeToShred = 0; // Set after processing complete
                document.Thumbprint = thumbprint;
                document.ModelId = modelId;
                document.ModelVersion = modelVersion;
                if (document.TaxDate != null && document.TaxDate.HasValue) {
                    document.TaxPeriod = document.TaxDate.Value.Year.ToString() + document.TaxDate.Value.Month.ToString();
                }

                // Lines

                for (int i = 1; i < ParsingConstants.MAX_DOCUMENT_LINES; i++)
                {
                    var lineNumber = i.ToString("D2");
                    string lineItemId = $"{ParsingConstants.LineItemPrefix}{lineNumber}";
                    string unitPriceId = $"{ParsingConstants.UnitPricePrefix}{lineNumber}";
                    string quantityId = $"{ParsingConstants.QuantityPrefix}{lineNumber}";
                    string netPriceId = $"{ParsingConstants.NetPricePrefix}{lineNumber}";
                    string vatCodeId = $"{ParsingConstants.VatCodePrefix}{lineNumber}";

                    // presence of any one of the following items will mean the document line is considered to exist.
                    string[] elements = { unitPriceId, netPriceId, lineItemId };

                    if (AnyElementsPresentForThisLine(nittyGritty, lineNumber, elements))
                    {
                        log.LogDebug($"{FUNCTION_NAME} {lineItemId}: {GetString(lineItemId, nittyGritty, document)}");
                        DocumentLineItem lineItem = new DocumentLineItem();
                        
                        // aid debug
                        string test = nittyGritty.ToString();
                        //
                        lineItem.ItemDescription = GetString(lineItemId, nittyGritty, document, DocumentErrorSeverity.Terminal);
                        lineItem.DocumentLineNumber = lineNumber;
                        lineItem.LineQuantity = GetNumber(quantityId, nittyGritty, document).ToString();
                        lineItem.NetAmount = GetNumber(netPriceId, nittyGritty, document, DocumentErrorSeverity.Terminal) ?? 0;
                        lineItem.UnitPrice = GetNumber(unitPriceId, nittyGritty, document, DocumentErrorSeverity.Terminal) ?? 0;
                        lineItem.VATCode = GetString(vatCodeId, nittyGritty, document, DocumentErrorSeverity.Warning);

                        document.LineItems.Add(lineItem);
                    }
                    else
                    {
                        break;
                    }
                }

                timer.Stop();
                document.TimeToShred = timer.ElapsedMilliseconds;
                string documentForOutput = "********";
                if (!string.IsNullOrEmpty(document.DocumentNumber)) documentForOutput = document.DocumentNumber;
                log.LogInformation($"{FUNCTION_NAME} Document {documentForOutput} was parsed form recognizer output in {document.TimeToShred} ms");
                
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.ThumbprintKey, thumbprint);
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, document.UniqueRunIdentifier);
                string documentName = $"{name.Substring(0, name.LastIndexOf('-'))}{ParsingConstants.DocumentExtension}";
                await utils.MoveBlobAsync(_processingContext.InboundDocumentContainer, _processingContext.ProcessingCompleteContainer, name);
                await utils.SaveDocumentAsync(document, _processingContext.ProcessingCompleteContainer, documentName, metadata);
                await SendToPersistenceQueue(document, log);
                log.LogInformation($"{FUNCTION_NAME} sucessfully processed incoming blob {name} in {document.TimeToShred} ms");
            }
            
            // unexpected failure 
            catch (Exception e)
            {
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, document.UniqueRunIdentifier);
                _telemetryClient.TrackException(e);
                log.LogError($"{FUNCTION_NAME} Unexpected error.  Exception Type: {e.GetType().ToString()} Message {e.Message}.  Please refer to application insights for diagnosis");
                string documentName = $"{name.Substring(0, name.LastIndexOf('-'))}{ParsingConstants.DocumentExtension}";
                await utils.MoveBlobAsync(_processingContext.InboundDocumentContainer, _processingContext.ExceptionContainer, documentName);
                await utils.SaveDocumentAsync(document, _processingContext.ExceptionContainer, name, metadata);
                await utils.SaveBlobAsync(_processingContext.ExceptionContainer, name + ParsingConstants.ExceptionExtension, e.ToString(), metadata);
                throw;
            }
            
        }

        private async Task SendToPersistenceQueue(Document document, ILogger log)
        {
            var payload = JsonConvert.SerializeObject(document); ;
            var messageModel = new MessageModel();
            messageModel.Payload = payload;
            messageModel.TraceGuid = document.UniqueRunIdentifier;
            messageModel.Queue = _processingContext.PersistenceQueueName;
            await _serviceBusSender.SendMessage(messageModel);
        }

        #region Document Parsing
        private static bool AnyElementsPresentForThisLine(JObject nittyGritty, string lineNumber, string[] elements)
        {

            foreach (JProperty child in nittyGritty.Children<JProperty>())
            {
                foreach (var elementName in elements)
                {

                    if (child.Name == elementName)
                    {
                        if (!string.IsNullOrEmpty(child.Value.ToString()))
                        {
                            return true;
                        }

                    }
                }
            }

            return false;
        }

        private static string SafeString(string input)
        {
            return input.Replace("'", ParsingConstants.IllegalCharacterMarker);
        }
        private static string GetString(string elementId, JObject nittyGritty, Document document, DocumentErrorSeverity severity = DocumentErrorSeverity.Warning)
        {
            string value;
            try
            {
                value = SafeString(nittyGritty[elementId]["text"].ToString());
            }
            catch (NullReferenceException)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0001", ErrorSeverity = severity, ErrorMessage = SafeString($"GetString() Specified Element {elementId} is null") });
                return null;
            }
            catch (Exception)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0001", ErrorSeverity = severity, ErrorMessage = SafeString($"GetString() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }
            return value;
        }

        private static Decimal? GetNumber(string elementId, JObject nittyGritty, Document document, DocumentErrorSeverity severity = DocumentErrorSeverity.Warning)
        {

            string numberAsString;
            try
            {
                numberAsString = nittyGritty[elementId]["text"].ToString();
            }
            catch (NullReferenceException)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0002", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() Specified Element {elementId} is null") });
                return null;
            }
            catch (Exception)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0002", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }

            if (numberAsString == null)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0003", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() {elementId} exists but its value is null") });
                return null;
            }

            if (Decimal.TryParse(numberAsString.Trim().Replace(" ", string.Empty), out decimal numberValue))
            {
                if (numberValue == 0)
                {
                    document.Errors.Add(new DocumentError { ErrorCode = "PRE0004", ErrorSeverity = DocumentErrorSeverity.Warning, ErrorMessage = SafeString($"GetNumber() {elementId} exists but its value is zero") });
                }

                return numberValue;
            }
            else
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0005", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() {elementId} exists but cannot be parsed as a number={numberAsString}") });
                return null;
            }

        }

        private static DateTime? GetDate(string elementId, JObject nittyGritty, Document document, DocumentErrorSeverity severity = DocumentErrorSeverity.Warning)
        {
            string dateAsString;
            try
            {
                dateAsString = nittyGritty[elementId]["text"].ToString();
            }
            catch (NullReferenceException)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0006", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} is null") });
                return null;
            }
            catch (Exception)
            {
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0006", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }

            DateTime dateValue;
            if (DateTime.TryParse(dateAsString, out dateValue))
                return dateValue;
            else
            {
                string safeDateAsString = dateAsString.Replace("'", ParsingConstants.IllegalCharacterMarker);
                document.Errors.Add(new DocumentError { ErrorCode = "PRE0007", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} does not contain a valid date: TaxDate={dateAsString}") });
                return null;
            }
        }
        #endregion

       
    }
}

