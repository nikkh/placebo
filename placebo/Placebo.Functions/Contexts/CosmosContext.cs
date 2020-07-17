using System;
using System.Collections.Generic;
using System.Text;

namespace Placebo.Functions.Contexts
{
    public class CosmosContext 
    {
        public string CosmosEndPointUrl { get;  set; }
        public string CosmosAuthorizationKey { get;  set; }
        public string CosmosDatabaseId { get;  set; }
        public string CosmosContainerId { get;  set; }
        
    }
}
