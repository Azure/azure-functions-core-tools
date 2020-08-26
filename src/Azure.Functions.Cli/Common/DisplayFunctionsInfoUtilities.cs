using System;
using System.Linq;
using Azure.Functions.Cli.Extensions;
using Colors.Net;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using static Colors.Net.StringStaticMethods;
using static Azure.Functions.Cli.Common.OutputTheme;
using Microsoft.Azure.WebJobs.Script.Description;
using System.Collections.Generic;

namespace Azure.Functions.Cli
{
    public static class DisplayFunctionsInfoUtilities
    {
        internal static void DisplayFunctionsInfo(ICollection<FunctionDescriptor> functions, HttpOptions httpOptions, Uri baseUri)
        {
                var allValidFunctions = functions.Where(f => !f.Metadata.IsDisabled());
                if (allValidFunctions.Any())
                {
                    ColoredConsole
                        .WriteLine()
                        .WriteLine(DarkYellow("Functions:"))
                        .WriteLine();
                }
                DisplayHttpFunctions(functions, httpOptions, baseUri);
                DisplayNonHttpFunctionsInfo(functions);
                DisplayDisabledFunctions(functions);
        }

        private static void DisplayHttpFunctions(ICollection<FunctionDescriptor> functions, HttpOptions httpOptions, Uri baseUri)
        {
            var httpFunctions = functions.Where(f => f.Metadata.IsHttpFunction() && !f.Metadata.IsDisabled());
            foreach (var function in httpFunctions)
            {
                var binding = function.Metadata.Bindings.FirstOrDefault(b => b.Type != null && b.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase));
                var httpRoute = binding?.Raw?.GetValue("route", StringComparison.OrdinalIgnoreCase)?.ToString();
                httpRoute = httpRoute ?? function.Name;

                string[] methods = null;
                var methodsRaw = binding?.Raw?.GetValue("methods", StringComparison.OrdinalIgnoreCase)?.ToString();
                if (string.IsNullOrEmpty(methodsRaw) == false)
                {
                    methods = methodsRaw.Split(',');
                }

                string hostRoutePrefix = "";
                if (!function.Metadata.IsProxy())
                {
                    hostRoutePrefix = httpOptions.RoutePrefix ?? "api/";
                    hostRoutePrefix = string.IsNullOrEmpty(hostRoutePrefix) || hostRoutePrefix.EndsWith("/")
                        ? hostRoutePrefix
                        : $"{hostRoutePrefix}/";
                }

                var functionMethods = methods != null ? $"{CleanAndFormatHttpMethods(string.Join(",", methods))}" : null;
                var url = $"{baseUri.ToString().Replace("0.0.0.0", "localhost")}{hostRoutePrefix}{httpRoute}";
                ColoredConsole
                    .WriteLine($"\t{HttpFunctionNameColor($"{function.Name}:")} {HttpFunctionUrlColor(functionMethods)} {HttpFunctionUrlColor(url)}")
                    .WriteLine();
            }
        }

        private static string CleanAndFormatHttpMethods(string httpMethods)
        {
            return httpMethods.Replace(Environment.NewLine, string.Empty).Replace(" ", string.Empty)
                .Replace("\"", string.Empty).ToUpperInvariant();
        }

        private static void DisplayNonHttpFunctionsInfo(ICollection<FunctionDescriptor> functions)
        {
                var nonHttpFunctions = functions.Where(f => !f.Metadata.IsHttpFunction() && !f.Metadata.IsDisabled());
                foreach (var function in nonHttpFunctions)
                {
                    var trigger = function.Metadata.Bindings.FirstOrDefault(b => b.Type != null && b.Type.EndsWith("Trigger", ignoreCase: true, null));
                    ColoredConsole
                        .WriteLine($"\t{Yellow($"{function.Name}:")} {trigger?.Type}")
                        .WriteLine();
                }
        }

        private static void DisplayDisabledFunctions(ICollection<FunctionDescriptor> functions)
        {
                foreach (var function in functions.Where(f => f.Metadata.IsDisabled()))
                {
                    ColoredConsole.WriteLine(WarningColor($"Function {function.Name} is disabled."));
                }
        }
    }
}
