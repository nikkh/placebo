using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Placebo.Functions.Models
{
    public class Document
    {
        private readonly string _id = Guid.NewGuid().ToString();
        public Document()
        {
            this.LineItems = new List<DocumentLineItem>();
            this.Errors = new List<DocumentError>();
            this.ShreddingUtcDateTime = DateTime.Now;
        }

        public string id { get { return _id; } }
        public string DocumentNumber { get; set; }
        public DateTime? TaxDate { get; set; }

        public string OrderNumber { get; set; }
        public string Account { get; set; }
        public DateTime? OrderDate { get; set; }

        public string FileName { get; set; }

        public DateTime ShreddingUtcDateTime { get; set; }

        public long TimeToShred { get; set; }
        [JsonProperty]
        public List<DocumentLineItem> LineItems { get; set; }
        [JsonProperty]
        public List<DocumentError> Errors { get; set; }
        
        public string RecognizerStatus { get; set; }
        public string RecognizerErrors { get; set; }
        public string UniqueRunIdentifier { get; set; }

        public int TerminalErrorCount { 
            get {
                return Errors.Where(x => x.ErrorSeverity == DocumentErrorSeverity.Terminal).Count();
            } 
        }
        public int WarningErrorCount {
            get {
                return Errors.Where(x => x.ErrorSeverity == DocumentErrorSeverity.Warning).Count();
            } 
        }
        public bool IsValid {
            get {

                if (TerminalErrorCount > 0) return false; else return true;
            } 
        }

        public void Print(bool terminalErrorsOnly = true)
        {

            Console.WriteLine($"Account: {Account}\nDocument Number:{DocumentNumber}\nDate: {TaxDate}\nOrder Number: {OrderNumber}\nDate: {OrderDate}");
            Console.WriteLine($"IsValid: {IsValid}\nTerminal Errors:{TerminalErrorCount}\nWarning Errors: {WarningErrorCount}");
            Console.WriteLine($"Shredding Time (ms) was {TimeToShred}");

            foreach (var item in LineItems)
            {
                Console.WriteLine($"{item.ItemDescription}\t{item.LineQuantity}\t{item.UnitPrice}\t{item.VATCode}\t{item.NetAmount}\t{item.CalculatedLineQuantity}");
            }
            Console.WriteLine();
            Console.WriteLine("Errors:");
            IEnumerable<DocumentError> errorsToPrint;
            if (terminalErrorsOnly)
            {
                errorsToPrint = Errors.Where(e => e.ErrorSeverity == DocumentErrorSeverity.Terminal);
            }
            else
            {
                errorsToPrint = Errors.AsEnumerable<DocumentError>();
            }
            if (errorsToPrint.Count() != 0)
            {
                foreach (var item in errorsToPrint)
                {
                    Console.WriteLine($"{item.ErrorCode}\t{item.ErrorMessage}\t{item.ErrorSeverity}");
                }
            }
            else
            {
                Console.WriteLine($"No errors. terminalErrorsOnlyFlag={terminalErrorsOnly}");
            }
        }

        public decimal VatAmount { get; set; }
        public decimal NetTotal { get; set; }
        public decimal GrandTotal { get; set; }
        public string PostCode { get; set; }

        public string Thumbprint { get; set; }
        public string TaxPeriod { get; set; }
        public string ModelId { get; set; }
        public string ModelVersion { get; set; }

        public string ToJsonString() 
        {
            return JsonConvert.SerializeObject(this);
        }


    }

    
}
