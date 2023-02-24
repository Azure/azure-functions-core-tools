using Azure.Functions.Cli.Interfaces;
using Newtonsoft.Json;
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
        private IList<string> _triggerHelpSupportedLanguages => new List<string>() { Languages.JavaScript, Languages.TypeScript };

        public async Task<string> GetTriggerHelp(string triggerName, string language)
        {
            if (_triggerHelp == null)
                _triggerHelp = await LoadTriggerHelp();

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

        private async Task<IDictionary<string, string>> LoadTriggerHelp()
        {
            var triggerNameList = new string[] {
                "BlobTrigger",
                "CosmosDBTrigger",
                "EventGridTrigger",
                "EventHubTrigger",
                "HttpTrigger",
                "StorageQueueTrigger",
                "ServiceBusQueueTrigger",
                "ServiceBusTopicTrigger",
                "TimerTrigger",
                "OrchestrationTrigger",
                "EntityTrigger"
            };

            var triggerHelpDictionary = new Dictionary<string, string>();

            foreach (var language in _triggerHelpSupportedLanguages)
            {
                foreach (var triggerName in triggerNameList)
                {
                    var triggerHelpContent = await StaticResources.GetValue($"{language}-{triggerName}-help.txt");
                    triggerHelpDictionary.Add(GetTriggerHelpKey(triggerName, language), triggerHelpContent);
                }
            }

            return triggerHelpDictionary;
        }

        private string GetTriggerHelpKey(string triggerName, string language)
        {
            return $"{triggerName?.Replace(" ", string.Empty)}.{language}".ToLower();
        }
    }
}
