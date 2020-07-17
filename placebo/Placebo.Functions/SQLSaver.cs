using System;
using System.Data.SqlClient;
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
    public class SQLSaver
    {
        private readonly IConfiguration _config;
        private readonly TelemetryClient _telemetryClient;
        private SQLContext _sqlContext;
        private readonly string _dbConnectionString;
        const string FUNCTION_NAME = "[SQLSaver]";

        public SQLSaver(IConfiguration config, TelemetryConfiguration telemetryConfig, SQLContext sqlContext)
        {
            _config = config;
            _sqlContext = sqlContext;
            _telemetryClient = new TelemetryClient(telemetryConfig);
            _dbConnectionString = _config.GetConnectionString("PlaceboDatabase");

        }

        [FunctionName("SQLSaver")]
        public async Task Run([ServiceBusTrigger("to-sql", Connection = "ServiceBusConnectionString")] Message message, ILogger log)
        {
            log.LogInformation($"{FUNCTION_NAME} function was triggered by receipt of service bus message");
            string payload = System.Text.Encoding.UTF8.GetString(message.Body);
            var messageModel = JsonConvert.DeserializeObject<MessageModel>(payload);
            var document = JsonConvert.DeserializeObject<Document>(messageModel.Payload);

            using (SqlConnection connection = new SqlConnection(_dbConnectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                SqlTransaction transaction;
                transaction = connection.BeginTransaction("PlaceboTransaction");
                command.Connection = connection;
                command.Transaction = transaction;

                try
                {
                    // Add the document 
                    string insertClause = $"Insert into Document (DocumentNumber, OrderNumber, FileName, ShreddingUtcDateTime, TimeToShred, RecognizerStatus, RecognizerErrors, UniqueRunIdentifier, TerminalErrorCount, WarningErrorCount, IsValid, Account, VatAmount, NetTotal, GrandTotal, PostCode, Thumbprint, TaxPeriod, ModelId, ModelVersion";
                    string valuesClause = $" VALUES ('{document.DocumentNumber}', '{document.OrderNumber}','{document.FileName}', '{document.ShreddingUtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}', '{document.TimeToShred}', '{document.RecognizerStatus}', '{document.RecognizerErrors}','{document.UniqueRunIdentifier}', '{document.TerminalErrorCount}','{document.WarningErrorCount}', '{document.IsValid}', '{document.Account}', '{document.VatAmount}', '{document.NetTotal}', '{document.GrandTotal}', '{document.PostCode}', '{document.Thumbprint}','{document.TaxPeriod}','{document.ModelId}','{document.ModelVersion}'";
                    if (document.TaxDate != null)
                    {
                        DateTime taxDate = (DateTime)document.TaxDate;
                        insertClause += ", TaxDate";
                        valuesClause += $", '{taxDate.ToString("yyyy-MM-dd HH:mm:ss.fff")}'";
                    }
                    if (document.OrderDate != null)
                    {
                        DateTime orderDate = (DateTime)document.OrderDate;
                        insertClause += ", OrderDate";
                        valuesClause += $", '{orderDate.ToString("yyyy-MM-dd HH:mm:ss.fff")}'";
                    }
                    insertClause += ") ";
                    valuesClause += ")";
                    command.CommandText = insertClause + valuesClause;


                    command.ExecuteNonQuery();
                    int currentIdentity = 0;
                    command.CommandText = "SELECT IDENT_CURRENT('[dbo].[Document]') AS Current_Identity";
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
                    foreach (var line in document.LineItems)
                    {
                        string safeDescription = null;
                        // ensure no single quotes in drug description
                        if (line.ItemDescription != null)
                        { safeDescription = line.ItemDescription.Replace("'", ParsingConstants.IllegalCharacterMarker); }
                        command.CommandText =
                        $"Insert into DocumentLineItem (DocumentId, ItemDescription, LineQuantity, UnitPrice, VATCode, NetAmount, CalculatedLineQuantity, DocumentLineNumber) " +
                        $"VALUES ('{currentIdentity}', '{safeDescription}', '{line.LineQuantity}','{line.UnitPrice}', '{line.VATCode}','{line.NetAmount}','{line.CalculatedLineQuantity}', '{line.DocumentLineNumber}')";
                        command.ExecuteNonQuery();
                    }

                    // Add the Errors
                    foreach (var error in document.Errors)
                    {
                        command.CommandText =
                        $"Insert into DocumentError (DocumentId, ErrorCode, ErrorSeverity, ErrorMessage) " +
                        $"VALUES ('{currentIdentity}', '{error.ErrorCode}', '{error.ErrorSeverity}','{error.ErrorMessage}' )";
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch (Exception e)
                {
                    log.LogError($"Exception prevented writing Document {document.DocumentNumber} to database (transaction was rolled back).  Message is {e.Message}");
                    transaction.Rollback();
                    throw e;
                }
                log.LogInformation($"{FUNCTION_NAME} Document {document.DocumentNumber} was written to SQL database {connection.Database}");
            }
          
        }
    }
}
