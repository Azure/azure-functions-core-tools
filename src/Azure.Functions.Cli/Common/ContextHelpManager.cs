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
        private IList<string> _triggerHelpSupportedLanguages => new List<string>() { Languages.Python, Languages.JavaScript, Languages.TypeScript };

        public async Task<string> GetTriggerHelp(string triggerName, string language)
        {
            if (_triggerHelp == null)
                _triggerHelp = await LoadTriggerHelp();

            var helpKey = GetTriggerHelpKey(triggerName, language);
            if (!_triggerHelp.ContainsKey(helpKey))
            {
                return string.Empty;
            }

            return _triggerHelp[helpKey];

        }

        private async Task<IDictionary<string, string>> LoadTriggerHelp()
        {
            var triggerNameList = new string[] {
                //"BlobTrigger",
                /*"CosmosDBTrigger",
                "EventHubTrigger",*/
                "HttpTrigger",
                /*"QueueTrigger",
                "ServiceBusQueueTrigger",
                "ServiceBusTopicTrigger",*/
                //"TimerTrigger"
            };

            var triggerHelpDictionary = new Dictionary<string, string>();

            foreach (var language in _triggerHelpSupportedLanguages)
            {
                foreach (var triggerName in triggerNameList)
                {
                    var triggerHelpContent = await StaticResources.GetValue($"{triggerName}-{language}-help.txt");
                    triggerHelpDictionary.Add(GetTriggerHelpKey(triggerName, language), triggerHelpContent);
                }
            }

            return triggerHelpDictionary;
        }

        private string GetTriggerHelpKey(string triggerName, string language)
        {
            return $"{triggerName}.{language}".ToLower();
        }



    }
}
