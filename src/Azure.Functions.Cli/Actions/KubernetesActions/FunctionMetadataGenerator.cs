using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class FunctionMetadataGenerator
{

    public static async Task GenerateFunctionsMetadata(string functionsPath)
    {
        var functionAppPath = Path.Combine(functionsPath, "function_app.py");

        var functionAppContents = await File.ReadAllTextAsync(functionAppPath);
        var functionMetadataList = ParsePythonFunctionApp(functionAppContents);
        var functionsMetadataJson = JsonConvert.SerializeObject(functionMetadataList, Formatting.Indented);
        var functionsMetadataPath = Path.Combine(functionsPath, "functions.metadata");
        await File.WriteAllTextAsync(functionsMetadataPath, functionsMetadataJson);
    }

    private static List<Dictionary<string, object>> ParsePythonFunctionApp(string contents)
    {
        var functionMetadataList = new List<Dictionary<string, object>>();
        var functionPattern = new Regex(@"@app\.(\w+)(?:_trigger)?\(([^)]+)\)\s+def (\w+)\(", RegexOptions.Compiled | RegexOptions.Multiline);
        var matches = functionPattern.Matches(contents);

        foreach (Match match in matches)
        {
            var triggerType = match.Groups[1].Value;
            var triggerProperties = match.Groups[2].Value;
            var functionName = match.Groups[3].Value;

            var properties = ParseTriggerProperties(triggerProperties);

            var bindings = new List<Dictionary<string, object>>();

            switch (triggerType)
            {
                case "route":
                    var authLevel = properties.GetValueOrDefault("auth_level", "Function")
                        .Split('.')
                        .LastOrDefault();
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", $"httpTrigger"},
                    {"direction", "In"},
                    {"name", "req"},
                    {"authLevel", authLevel},
                    {"methods", new[] {"get", "post"}}, // Assuming default methods are get and post
                });
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "http"},
                    {"direction", "Out"},
                    {"name", "$return"}
                });
                    break;
                case "timer_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", $"timerTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "myTimer")},
                    {"schedule", properties["schedule"]}
                });
                    break;
                case "blob_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", $"blobTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "stream")},
                    {"path", properties["path"]},
                    {"connection", properties["connection"]},
                    {"properties", new Dictionary<string, object> { { "supportsDeferredBinding", "True" } } }
                });
                    break;
                case "cosmos_db_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "cosmosDBTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "input")},
                    {"databaseName", properties["database_name"]},
                    {"containerName", properties["container_name"]},
                    {"connection", properties["connection"]},
                    {"leaseContainerName", "leases"},
                    {"createLeaseContainerIfNotExists", true}
                });
                    break;
                case "event_hub_message_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "eventHubTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "events")},
                    {"eventHubName", properties["event_hub_name"]},
                    {"connection", properties["connection"]},
                    {"cardinality", "Many"},
                    {"properties", new Dictionary<string, object> { { "supportsDeferredBinding", "True" } } }
                });
                    break;
                case "queue_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "queueTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "message")},
                    {"queueName", properties["queue_name"]},
                    {"connection", properties["connection"]},
                    {"properties", new Dictionary<string, object> { { "supportsDeferredBinding", "True" } } }
                });
                    break;
                case "service_bus_queue_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "serviceBusTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "message")},
                    {"queueName", properties["queue_name"]},
                    {"connection", properties["connection"]},
                    {"cardinality", "One"},
                    {"properties", new Dictionary<string, object> { { "supportsDeferredBinding", "True" } } }
                });
                    break;
                case "service_bus_topic_trigger":
                    bindings.Add(new Dictionary<string, object>
                {
                    {"type", "serviceBusTrigger"},
                    {"direction", "In"},
                    {"name", properties.GetValueOrDefault("arg_name", "message")},
                    {"topicName", properties["topic_name"]},
                    {"subscriptionName", properties["subscription_name"]},
                    {"connection", properties["connection"]},
                    {"cardinality", "One"},
                    {"properties", new Dictionary<string, object> { { "supportsDeferredBinding", "True" } } }
                });
                    break;
                    // Add additional cases for other trigger types if necessary
            }

            functionMetadataList.Add(new Dictionary<string, object>
        {
            {"name", functionName},
            {"bindings", bindings}
        });
        }

        return functionMetadataList;
    }


    private static Dictionary<string, string> ParseTriggerProperties(string properties)
    {
        var propertiesDict = new Dictionary<string, string>();
        var propPattern = new Regex(@"(\w+)=(?:""([^""]*)""|'([^']*)'|([^,\s]+))", RegexOptions.Compiled);
        var propMatches = propPattern.Matches(properties);

        foreach (Match propMatch in propMatches)
        {
            var key = propMatch.Groups[1].Value;
            var value = propMatch.Groups[2].Success ? propMatch.Groups[2].Value :
                        propMatch.Groups[3].Success ? propMatch.Groups[3].Value :
                        propMatch.Groups[4].Value;
            propertiesDict[key] = value;
        }

        return propertiesDict;
    }

}
