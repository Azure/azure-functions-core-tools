using Dynamitey;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public class FunctionApp
    {
        public string Name { get; set; }
        public FunctionAppOs Os { get; set; }
        public FunctionAppSku Sku { get; set; }
        public FunctionAppRuntime Runtime { get; set; }
        public FunctionAppLocation Location { get; set; }
    }
}
