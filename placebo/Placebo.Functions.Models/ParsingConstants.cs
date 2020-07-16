using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Models
{
    public class ParsingConstants
    {
        public const string TaxDate = "TaxDate";
        public const string OrderNumber = "OrderNO";
        public const string OrderDate = "OrderDate";
        public const string InvoiceNumber = "Inv";
        public const string Account = "AccountNo";
        public const string LineItemPrefix = "Drug";
        public const int MAX_DOCUMENT_LINES = 50;
        public const string QuantityPrefix = "Qty";
        public const string UnitPricePrefix = "Unit";
        public const string NetPricePrefix = "Net";
        public const string VatCodePrefix = "Vat";
        public const string VatAmount = "VAT";
        public const string NetTotal = "Total";
        public const string GrandTotal = "TotalIncVAT";
        public const string PostCode = "PostCode";
        public const string IllegalCharacterMarker = "@Illegal@";
        public const string UniqueProcessingIdKey = "UniqueProcessingId";
        public const string InvoiceFormatKey = "InvoiceFormat";
        public const string RecognizedExtension = "-recognized.json";
        public const string ExceptionExtension = "-exception.json";
        public const string InvoiceExtension = "-invoice.json";
        public const string DocumentExtension = "-document.json";
        public const string OcpApimSubscriptionKey = "Ocp-Apim-Subscription-Key";
        public const string FormRecognizerApiPath = "formrecognizer/v2.0-preview/custom/models";
        public const string FormRecognizerAnalyzeVerb = "analyze";
        public static readonly string UniqueRunIdentifierKey = "UniqueRunIdentifier";
        public static readonly string TelemetryOperationParentIdKey = "TelemetryParentId";
        public static readonly string TelemetryOperationIdKey = "TelemetryOperationId";
        public static readonly string ThumbprintKey = "Thumbprint";
        public static readonly string ModelIdKey = "ModelId";
        public static readonly string ModelVersionKey = "ModelVersion";
        public const int MaxRetriesForBlobLease = 15;

       
    }
}