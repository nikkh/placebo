using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Models
{
    public class DocumentError
    {
        public string ErrorCode { get; set; }
        public DocumentErrorSeverity ErrorSeverity { get; set; }

        public string ErrorMessage { get; set; }
      
    }

    public enum DocumentErrorSeverity { Observation, Warning, Terminal}
}
