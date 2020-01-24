using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using static Azure.Functions.Cli.Common.OutputTheme;
using System.Collections.Generic;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "logstream", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Show interactive streaming logs for an Azure-hosted Function App")]
    class LogStreamAction : BaseFunctionAppAction
    {
        private const string ApplicationInsightsIKeySetting = "APPINSIGHTS_INSTRUMENTATIONKEY";
        private const string LiveMetricsUriTemplate = "https://portal.azure.com/#blade/AppInsightsExtension/QuickPulseBladeV2/ComponentId/{0}/ResourceId/{1}";

        public bool UseBrowser { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("browser")
                .WithDescription("Open Azure Application Insights Live Stream in a browser.")
                .SetDefault(false)
                .Callback(s => UseBrowser = s);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var subscriptions = await AzureHelper.GetSubscriptions(AccessToken, ManagementURL);
            ColoredConsole.WriteLine("Retrieving Function App...");
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken, ManagementURL, Slot, Subscription, allSubs: subscriptions);
            if (UseBrowser)
            {
                await OpenLiveStreamInBrowser(functionApp, subscriptions);
                return;
            }

            if (functionApp.IsLinux && functionApp.IsDynamic)
            {
                throw new CliException("Log stream is not currently supported in Linux Consumption Apps. " +
                    "Please use --browser to open Azure Application Insights Live Stream in the Azure portal.");
            }
            var basicHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{functionApp.PublishingUserName}:{functionApp.PublishingPassword}"));

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicHeaderValue);
                client.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
                var response = await client.GetStreamAsync(new Uri($"https://{functionApp.ScmUri}/api/logstream/application"));
                using (var reader = new StreamReader(response))
                {
                    var buffer = new char[4096];
                    var count = 0;
                    do
                    {
                        count = await reader.ReadAsync(buffer, 0, buffer.Length);
                        ColoredConsole.Write(new string(buffer.Take(count).ToArray()));
                    } while (count != 0);
                }
            }
        }

        public async Task OpenLiveStreamInBrowser(Site functionApp, IEnumerable<ArmSubscription> allSubscriptions)
        {
            if (!functionApp.AzureAppSettings.ContainsKey(ApplicationInsightsIKeySetting))
            {
                throw new CliException($"Missing {ApplicationInsightsIKeySetting} App Setting. " +
                    $"Please make sure you have Application Insights configured with you function app.");
            }

            var iKey = functionApp.AzureAppSettings[ApplicationInsightsIKeySetting];
            if (string.IsNullOrEmpty(iKey))
            {
                throw new CliException("Invalid Instrumentation Key found. Please make sure that the Application Insights is configured correctly.");
            }

            ColoredConsole.WriteLine("Retrieving Application Insights information...");
            var appId = await AzureHelper.GetApplicationInsightIDFromIKey(iKey, AccessToken, ManagementURL, allSubs: allSubscriptions);
            var armResourceId = AzureHelper.ParseResourceId(appId);
            var componentId = $@"{{""Name"":""{armResourceId.Name}"",""SubscriptionId"":""{armResourceId.Subscription}"",""ResourceGroup"":""{armResourceId.ResourceGroup}""}}";

            var liveMetricsUrl = string.Format(LiveMetricsUriTemplate, WebUtility.UrlEncode(componentId), WebUtility.UrlEncode(appId));

            ColoredConsole.WriteLine("Launching web browser...");
            if (StaticSettings.IsDebug)
            {
                ColoredConsole.WriteLine(VerboseColor($"Launching browser with URL- {liveMetricsUrl}"));
            }

            Utilities.OpenBrowser(liveMetricsUrl);
        }
    }
}
