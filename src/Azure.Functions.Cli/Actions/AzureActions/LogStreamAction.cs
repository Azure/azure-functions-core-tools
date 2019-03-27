using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "logstream", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Show interactive streaming logs for an Azure-hosted Function App")]
    class LogStreamAction : BaseFunctionAppAction
    {
        public override async Task RunAsync()
        {
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken);

            if (functionApp.IsLinux && functionApp.IsDynamic)
            {
                throw new CliException("Log stream is not currently supported in Linux Consumption Apps. Please use Azure Application Insights Live Stream in the Azure portal.");
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
    }
}
