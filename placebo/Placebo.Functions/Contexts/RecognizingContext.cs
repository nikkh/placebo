using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Contexts
{
    public class RecognizingContext 
    {
        public string InboundImageContainer { get; set; }
        public string OutboundJsonContainer { get; set; }
        public string ExceptionContainer { get; set; }
        public int MaxRetries { get; set; }
        public string ProcessingCompleteContainer { get; set; }
        
    }

    
}
