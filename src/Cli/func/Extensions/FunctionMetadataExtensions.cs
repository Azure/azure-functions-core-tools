using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Azure.Functions.Cli.Extensions
{
    internal static class FunctionMetadataExtensions
    {
        public static bool IsHttpFunction(this FunctionMetadata functionMetadata)
        {
            return functionMetadata
                .InputBindings
                .Any(i => i.IsTrigger && i.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase));
        }
    }
}
