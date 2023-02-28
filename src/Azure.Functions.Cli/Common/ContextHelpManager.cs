using Azure.Functions.Cli.Interfaces;
using Microsoft.AspNetCore.Server.IIS.Core;
using Newtonsoft.Json;
using NuGet.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Azure.Functions.Cli.Common.Constants;

namespace Azure.Functions.Cli.Common
{
    public class ContextHelpManager : IContextHelpManager
    {
        private IDictionary<string, string> _triggerHelp;
        private IDictionary<string, string> _triggerNameMap;
        private IList<string> _triggerHelpSupportedLanguages => new List<string>() { Languages.JavaScript, Languages.TypeScript, Languages.Python };

        public string GetTriggerHelp(string triggerName, string language)
        {
            var helpKey = GetTriggerHelpKey(triggerName, language);
            if (!_triggerHelp.ContainsKey(helpKey))
            {
                return string.Empty;
            }

            var triggerHelp = _triggerHelp[helpKey];
            if (language == Languages.JavaScript || language == Languages.TypeScript)
            {
                triggerHelp = $"{triggerHelp}{Environment.NewLine}{Environment.NewLine}Programming model v4 for Node is currently in preview. The goal of this model is to introduce a more intuitive and idiomatic way of authoring Function triggers and bindings for JavaScript and TypeScript developers. Learn more http://aka.ms/AzFuncNodeV4. ";
            }

            return triggerHelp;
        }

        public async Task LoadTriggerHelp(string language, List<string> triggerNames)
        {
            CreateTemplateMapForHelp(triggerNames);

            if (!_triggerHelpSupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                throw new CliException("Only Python, JavaScript and TypeScript support this help command at the moment.");
            }

            _triggerHelp = new Dictionary<string, string>();
            foreach (var triggerKeyValue in _triggerNameMap)
            {
                var triggerHelpContent = await StaticResources.GetValue($"{language?.ToLower()}-{triggerKeyValue.Value}-help.txt");
                _triggerHelp.Add(GetTriggerHelpKey(triggerKeyValue.Value, language), triggerHelpContent);
            }
        }

        public bool IsValidTriggerNameForHelp(string triggerName)
        {
            return _triggerNameMap.Keys.Any(x => x.Equals(triggerName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsValidTriggerTypeForHelp(string triggerName)
        {
            return _triggerNameMap.Values.Any(x => x.Equals(triggerName, StringComparison.OrdinalIgnoreCase));
        }

        public string GetTriggerTypeFromTriggerNameForHelp(string triggerName)
        {
            return _triggerNameMap.FirstOrDefault(x => x.Key.Equals(triggerName, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private void CreateTemplateMapForHelp(List<string> triggerNames)
        {
            var map = new Dictionary<string, string>
            {
                { "Azure Blob Storage trigger", "BlobTrigger" },
                { "Azure Cosmos DB trigger", "CosmosDBTrigger" },
                { "Durable Functions entity", "EntityTrigger" },
                { "Durable Functions orchestrator", "OrchestrationTrigger" },
                { "Azure Event Grid trigger", "EventGridTrigger" },
                { "Azure Event Hub trigger", "EventHubTrigger" },
                { "HTTP trigger", "HttpTrigger" },
                { "Azure Queue Storage trigger", "StorageQueueTrigger" },
                { "Azure Service Bus Queue trigger", "ServiceBusQueueTrigger" },
                { "Azure Service Bus Topic trigger", "ServiceBusTopicTrigger" },
                { "Timer trigger", "TimerTrigger" }
            };

            _triggerNameMap = map.Where(kvp => triggerNames.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private string GetTriggerHelpKey(string triggerName, string language)
        {
            return $"{triggerName?.Replace(" ", string.Empty)}.{language}".ToLower();
        }
    }
}