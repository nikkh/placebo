using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Models
{
    public class TrainingRequestMessage
    {
        public decimal AverageModelAccuracy { get; set; }

        public string BlobSasUrl { get; set; }
        public string BlobFolderName { get; set; }
        public string IncludeSubFolders { get; set; }
        public string UseLabelFile { get; set; }
        public string DocumentFormat { get; set; }
        public string ModelId { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public DateTime UpdatedDateTime { get; set; }
        public string TrainingDocumentResults { get; set; }
    }
}
