using System;
using System.Linq;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Azure.Functions.Cli.Extensions
{
    internal static class FunctionStatusExtensions
    {
        public static bool IsHttpFunction(this FunctionStatus functionStatus)
        {
            return functionStatus
                ?.Metadata
                ?.InputBindings
                .Any(i => i.IsTrigger && i.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase)) ?? false;
        }
    }
}
