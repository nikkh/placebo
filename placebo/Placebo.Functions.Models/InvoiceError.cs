using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Models
{
    public class InvoiceError
    {
        public string ErrorCode { get; set; }
        public InvoiceErrorSeverity ErrorSeverity { get; set; }

        public string ErrorMessage { get; set; }
      
    }

    public enum InvoiceErrorSeverity { Observation, Warning, Terminal}
}
