using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Contexts
{
    public class ProcessingContext 
    {
       
        public string OutputInvoiceContainer { get; set; }
        public string InboundDocumentContainer { get; set; }
        public string InvestigateContainer { get; set; }
        public string ExceptionContainer { get; set; }
        public string ProcessingCompleteContainer { get; set; }
    }
}
