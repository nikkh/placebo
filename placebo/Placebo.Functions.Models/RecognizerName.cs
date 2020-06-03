using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Models
{
    public class RecognizerName
    {
        public string InvoiceFormat { get; }
        public string Name { get; }

        public string ImageType { get; }

        public RecognizerName(string name)
        {
            Name = name;
            var hyphenPos = name.IndexOf('-');
            InvoiceFormat = name.Substring(0, hyphenPos).ToLower();
            var dotPos = name.LastIndexOf('.') + 1;
            ImageType = name.Substring(dotPos, name.Length - dotPos).ToLower();
        }
        private RecognizerName(){}
    }
}
