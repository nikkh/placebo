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

namespace Placebo.Functions
{
    public class Processor
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _dbConnectionString;
        private ProcessingContext _processingContext;
        const string FUNCTION_NAME = "[Processor]";

       public Processor(IConfiguration config, TelemetryConfiguration telemetryConfig, ProcessingContext processingContext)
        {
            _config = config;
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");
            _processingContext = processingContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);

        }

        [FunctionName("Processor")]
        public async Task Run([BlobTrigger("disabled-process-in-json/{name}", Connection = "IncomingConnection")]Stream incomingBlobStream, string name,  ILogger log, ExecutionContext context)
        {
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of blob - Name:{name} Size: {incomingBlobStream.Length} Bytes Container: {_processingContext.InboundDocumentContainer}");
            string thumbprint = "";
            string modelId = "";
            string modelVersion = "";
            string operationId = "";
            string parentId = "";
            Utils utils = new Utils(log, _config["IncomingConnection"], FUNCTION_NAME);
            
            IDictionary<string, string> metadata = new Dictionary<string, string>();
            Invoice invoice = new Invoice { FileName = name };
            invoice.UniqueRunIdentifier = Guid.NewGuid().ToString();
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
                                invoice.UniqueRunIdentifier = metadata[ParsingConstants.UniqueRunIdentifierKey];
                                log.LogDebug($"{FUNCTION_NAME} unique run identifier was set to  {invoice.UniqueRunIdentifier}");
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
                if (jsonContent["status"] != null) invoice.RecognizerStatus = jsonContent["status"].ToString();
                if (jsonContent["errors"] != null) invoice.RecognizerErrors = jsonContent["errors"].ToString();

                // Fill out the invoice object
                var nittyGritty = (JObject)jsonContent["analyzeResult"]["documentResults"][0]["fields"];
                log.LogInformation($"{FUNCTION_NAME} Creating invoice based on forms recognizer output");
                invoice.ShreddingUtcDateTime = DateTime.Now;
                invoice.OrderNumber = GetString(ParsingConstants.OrderNumber, nittyGritty, invoice);
                invoice.OrderDate = GetDate(ParsingConstants.OrderDate, nittyGritty, invoice);
                invoice.TaxDate = GetDate(ParsingConstants.TaxDate, nittyGritty, invoice);
                invoice.InvoiceNumber = GetString(ParsingConstants.InvoiceNumber, nittyGritty, invoice);
                invoice.Account = GetString(ParsingConstants.Account, nittyGritty, invoice);
                invoice.NetTotal = GetNumber(ParsingConstants.NetTotal, nittyGritty, invoice) ?? 0;
                invoice.VatAmount = GetNumber(ParsingConstants.VatAmount, nittyGritty, invoice) ?? 0;
                invoice.GrandTotal = GetNumber(ParsingConstants.GrandTotal, nittyGritty, invoice) ?? 0;
                invoice.PostCode = GetString(ParsingConstants.PostCode, nittyGritty, invoice);
                invoice.TimeToShred = 0; // Set after processing complete
                invoice.Thumbprint = thumbprint;
                invoice.ModelId = modelId;
                invoice.ModelVersion = modelVersion;
                if (invoice.TaxDate != null && invoice.TaxDate.HasValue) {
                    invoice.TaxPeriod = invoice.TaxDate.Value.Year.ToString() + invoice.TaxDate.Value.Month.ToString();
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

                    // presence of any one of the following items will mean the invoice line is considered to exist.
                    string[] elements = { unitPriceId, netPriceId, lineItemId };

                    if (AnyElementsPresentForThisLine(nittyGritty, lineNumber, elements))
                    {
                        log.LogDebug($"{FUNCTION_NAME} {lineItemId}: {GetString(lineItemId, nittyGritty, invoice)}");
                        InvoiceLineItem lineItem = new InvoiceLineItem
                        {
                            //ItemDescription = GetString(lineItemId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal),
                            //InvoiceLineNumber = lineNumber,
                            //LineQuantity = GetNumber(quantityId, nittyGritty, invoice).ToString(),
                            //NetAmount = GetNumber(netPriceId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal) ?? 0,
                            //UnitPrice = GetNumber(unitPriceId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal) ?? 0,
                            //VATCode = GetString(vatCodeId, nittyGritty, invoice, InvoiceErrorSeverity.Warning),
                        };
                        // aid debug
                        string test = nittyGritty.ToString();
                        //
                        lineItem.ItemDescription = GetString(lineItemId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal);
                        lineItem.InvoiceLineNumber = lineNumber;
                        lineItem.LineQuantity = GetNumber(quantityId, nittyGritty, invoice).ToString();
                        lineItem.NetAmount = GetNumber(netPriceId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal) ?? 0;
                        lineItem.UnitPrice = GetNumber(unitPriceId, nittyGritty, invoice, InvoiceErrorSeverity.Terminal) ?? 0;
                        lineItem.VATCode = GetString(vatCodeId, nittyGritty, invoice, InvoiceErrorSeverity.Warning);

                        invoice.LineItems.Add(lineItem);
                    }
                    else
                    {
                        break;
                    }
                }

                timer.Stop();
                invoice.TimeToShred = timer.ElapsedMilliseconds;
                string invoiceForOutput = "********";
                if (!string.IsNullOrEmpty(invoice.InvoiceNumber)) invoiceForOutput = invoice.InvoiceNumber;
                log.LogInformation($"{FUNCTION_NAME} Invoice {invoiceForOutput} was parsed form recognizer output in {invoice.TimeToShred} ms");
                
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.ThumbprintKey, thumbprint);
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, invoice.UniqueRunIdentifier);
                string invoiceName = $"{name.Substring(0, name.LastIndexOf('-'))}{ParsingConstants.InvoiceExtension}";
                await utils.MoveBlobAsync(_processingContext.InboundDocumentContainer, _processingContext.ProcessingCompleteContainer, name);
                await utils.SaveInvoiceAsync(invoice, _processingContext.ProcessingCompleteContainer, invoiceName, metadata);
                UpdateDatabase(invoice, log);
                log.LogInformation($"{FUNCTION_NAME} sucessfully processed incoming blob {name} in {invoice.TimeToShred} ms");
            }
            
            // unexpected failure 
            catch (Exception e)
            {
                metadata = new Dictionary<string, string>();
                metadata.Add(ParsingConstants.UniqueRunIdentifierKey, invoice.UniqueRunIdentifier);
                _telemetryClient.TrackException(e);
                log.LogError($"{FUNCTION_NAME} Unexpected error.  Exception Type: {e.GetType().ToString()} Message {e.Message}.  Please refer to application insights for diagnosis");
                string invoiceName = $"{name.Substring(0, name.LastIndexOf('-'))}{ParsingConstants.InvoiceExtension}";
                await utils.MoveBlobAsync(_processingContext.InboundDocumentContainer, _processingContext.ExceptionContainer, invoiceName);
                await utils.SaveInvoiceAsync(invoice, _processingContext.ExceptionContainer, name, metadata);
                await utils.SaveBlobAsync(_processingContext.ExceptionContainer, name + ParsingConstants.ExceptionExtension, e.ToString(), metadata);
                throw;
            }
            
        }

        #region Parsing methods
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

        private static string GetString(string elementId, JObject nittyGritty, Invoice invoice, InvoiceErrorSeverity severity = InvoiceErrorSeverity.Warning)
        {
            string value;
            try
            {
                value = SafeString(nittyGritty[elementId]["text"].ToString());
            }
            catch (NullReferenceException)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0001", ErrorSeverity = severity, ErrorMessage = SafeString($"GetString() Specified Element {elementId} is null" )});
                return null;
            }
            catch (Exception)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0001", ErrorSeverity = severity, ErrorMessage = SafeString($"GetString() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }
            return value;
        }

        private static Decimal? GetNumber(string elementId, JObject nittyGritty, Invoice invoice, InvoiceErrorSeverity severity = InvoiceErrorSeverity.Warning)
        {

            string numberAsString;
            try
            {
                numberAsString = nittyGritty[elementId]["text"].ToString();
            }
            catch (NullReferenceException)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0002", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() Specified Element {elementId} is null") });
                return null;
            }
            catch (Exception)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0002", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }

            if (numberAsString == null)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0003", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() {elementId} exists but its value is null") });
                return null;
            }

            if (Decimal.TryParse(numberAsString.Trim().Replace(" ", string.Empty), out decimal numberValue))
            {
                if (numberValue == 0)
                {
                    invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0004", ErrorSeverity = InvoiceErrorSeverity.Warning, ErrorMessage = SafeString($"GetNumber() {elementId} exists but its value is zero") });
                }

                return numberValue;
            }
            else
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0005", ErrorSeverity = severity, ErrorMessage = SafeString($"GetNumber() {elementId} exists but cannot be parsed as a number={numberAsString}") });
                return null;
            }

        }

        private static DateTime? GetDate(string elementId, JObject nittyGritty, Invoice invoice, InvoiceErrorSeverity severity = InvoiceErrorSeverity.Warning)
        {
            string dateAsString;
            try
            {
                dateAsString = nittyGritty[elementId]["text"].ToString();
            }
            catch (NullReferenceException)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0006", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} is null") });
                return null;
            }
            catch (Exception)
            {
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0006", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} does not exist in recognized output") });
                return null;
            }

            DateTime dateValue;
            if (DateTime.TryParse(dateAsString, out dateValue))
                return dateValue;
            else
            {
                string safeDateAsString = dateAsString.Replace("'", ParsingConstants.IllegalCharacterMarker);
                invoice.Errors.Add(new InvoiceError { ErrorCode = "PRE0007", ErrorSeverity = severity, ErrorMessage = SafeString($"GetDate() Specified Element {elementId} does not contain a valid date: TaxDate={dateAsString}") });
                return null;
            }
        }

        private static string SafeString(string input)
        {
            return input.Replace("'", ParsingConstants.IllegalCharacterMarker);
        }
        #endregion

        #region Database Methods 
        private void UpdateDatabase(Invoice invoice, ILogger log)
        {

            using (SqlConnection connection = new SqlConnection(_dbConnectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                SqlTransaction transaction;
                transaction = connection.BeginTransaction("ShredderTransaction");
                command.Connection = connection;
                command.Transaction = transaction;

                try
                {
                    // Add the Invoice 
                    string insertClause = $"Insert into Invoice (InvoiceNumber, OrderNumber, FileName, ShreddingUtcDateTime, TimeToShred, RecognizerStatus, RecognizerErrors, UniqueRunIdentifier, TerminalErrorCount, WarningErrorCount, IsValid, Account, VatAmount, NetTotal, GrandTotal, PostCode, Thumbprint, TaxPeriod, ModelId, ModelVersion";
                    string valuesClause = $" VALUES ('{invoice.InvoiceNumber}', '{invoice.OrderNumber}','{invoice.FileName}', '{invoice.ShreddingUtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}', '{invoice.TimeToShred}', '{invoice.RecognizerStatus}', '{invoice.RecognizerErrors}','{invoice.UniqueRunIdentifier}', '{invoice.TerminalErrorCount}','{invoice.WarningErrorCount}', '{invoice.IsValid}', '{invoice.Account}', '{invoice.VatAmount}', '{invoice.NetTotal}', '{invoice.GrandTotal}', '{invoice.PostCode}', '{invoice.Thumbprint}','{invoice.TaxPeriod}','{invoice.ModelId}','{invoice.ModelVersion}'";
                    if (invoice.TaxDate != null)
                    {
                        DateTime taxDate = (DateTime)invoice.TaxDate;
                        insertClause += ", TaxDate";
                        valuesClause += $", '{taxDate.ToString("yyyy-MM-dd HH:mm:ss.fff")}'";
                    }
                    if (invoice.OrderDate != null)
                    {
                        DateTime orderDate = (DateTime)invoice.OrderDate;
                        insertClause += ", OrderDate";
                        valuesClause += $", '{orderDate.ToString("yyyy-MM-dd HH:mm:ss.fff")}'";
                    }
                    insertClause += ") ";
                    valuesClause += ")";
                    command.CommandText = insertClause + valuesClause;


                    command.ExecuteNonQuery();
                    int currentIdentity = 0;
                    command.CommandText = "SELECT IDENT_CURRENT('[dbo].[Invoice]') AS Current_Identity";
                    SqlDataReader reader = command.ExecuteReader();
                    try
                    {
                        while (reader.Read())
                        {
                            currentIdentity = Convert.ToInt32(reader["Current_Identity"]);
                        }

                    }
                    finally
                    {
                        reader.Close();
                    }

                    // Add the lines
                    foreach (var line in invoice.LineItems)
                    {
                        string safeDescription = null;
                        // ensure no single quotes in drug description
                        if (line.ItemDescription != null)
                        { safeDescription = line.ItemDescription.Replace("'", ParsingConstants.IllegalCharacterMarker); }
                        command.CommandText =
                        $"Insert into InvoiceLineItem (InvoiceId, ItemDescription, LineQuantity, UnitPrice, VATCode, NetAmount, CalculatedLineQuantity, InvoiceLineNumber) " +
                        $"VALUES ('{currentIdentity}', '{safeDescription}', '{line.LineQuantity}','{line.UnitPrice}', '{line.VATCode}','{line.NetAmount}','{line.CalculatedLineQuantity}', '{line.InvoiceLineNumber}')";
                        command.ExecuteNonQuery();
                    }

                    // Add the Errors
                    foreach (var error in invoice.Errors)
                    {
                        command.CommandText =
                        $"Insert into InvoiceError (InvoiceId, ErrorCode, ErrorSeverity, ErrorMessage) " +
                        $"VALUES ('{currentIdentity}', '{error.ErrorCode}', '{error.ErrorSeverity}','{error.ErrorMessage}' )";
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch (Exception e)
                {
                    log.LogError($"Exception prevented writing invoice {invoice.InvoiceNumber} to database (transaction was rolled back).  Message is {e.Message}");
                    transaction.Rollback();
                    throw e;
                }

                log.LogInformation($"{FUNCTION_NAME} Invoice {invoice.InvoiceNumber} was written to the database");


            }
        }
        #endregion

    }
}

