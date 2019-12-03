using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal class KuduLiteDeploymentHelpers
    {
        public static async Task<Dictionary<string, string>> GetAppSettings(HttpClient client)
        {
            return await InvokeRequest<Dictionary<string, string>>(client, HttpMethod.Get, "/api/settings");
        }

        public static async Task<DeployStatus> WaitForConsumptionServerSideBuild(HttpClient client, Site functionApp, string accessToken, string managementUrl)
        {
            ColoredConsole.WriteLine("Remote build in progress, please wait...");
            DeployStatus statusCode = DeployStatus.Pending;
            DateTime logLastUpdate = DateTime.MinValue;
            string id = null;

            while (string.IsNullOrEmpty(id))
            {
                id = await GetLatestDeploymentId(client, functionApp);
                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed)
            {
                statusCode = await GetDeploymentStatusById(client, functionApp, id);
                logLastUpdate = await DisplayDeploymentLog(client, functionApp, id, logLastUpdate);
                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            return statusCode;
        }

        public static async Task<DeployStatus> WaitForDedicatedBuildToComplete(HttpClient client, Site functionApp)
        {
            // There is a tracked Locking issue in kudulite causing Race conditions, so we have to use this API
            // to gather deployment progress.
            ColoredConsole.Write("Remote build in progress, please wait");
            while (true)
            {
                var json = await InvokeRequest<IDictionary<string, string>>(client, HttpMethod.Get, "/api/isdeploying");

                if (bool.TryParse(json["value"], out bool isDeploying))
                {
                    if (!isDeploying)
                    {
                        string deploymentId = await GetLatestDeploymentId(client, functionApp);
                        DeployStatus status = await GetDeploymentStatusById(client, functionApp, id: deploymentId);
                        ColoredConsole.Write($"done{Environment.NewLine}");
                        return status;
                    }
                }
                else
                {
                    throw new CliException($"Expected \"value\" from /api/isdeploying endpoing to be a boolean. Actual: {json["value"]}");
                }

                ColoredConsole.Write(".");
                await Task.Delay(5000);
            }
        }

        private static async Task<string> GetLatestDeploymentId(HttpClient client, Site functionApp)
        {
            var json = await InvokeRequest<List<Dictionary<string, string>>>(client, HttpMethod.Get, "/deployments");

            // Automatically ordered by received time
            var latestDeployment = json.First();
            if (latestDeployment.TryGetValue("status", out string statusString))
            {
                DeployStatus status = ConvertToDeployementStatus(statusString);
                if (status != DeployStatus.Pending)
                {
                    return latestDeployment["id"];
                }
            }
            return null;
        }

        private static async Task<DeployStatus> GetDeploymentStatusById(HttpClient client, Site functionApp, string id)
        {
            var json = await InvokeRequest<Dictionary<string, string>>(client,
                HttpMethod.Get, $"/deployments/{id}");

            if (json.TryGetValue("status", out string statusString))
            {
                return ConvertToDeployementStatus(json["status"]);
            }
            return DeployStatus.Failed;
        }

        private static async Task<DateTime> DisplayDeploymentLog(HttpClient client, Site functionApp, string id, DateTime lastUpdate, Uri innerUrl = null)
        {
            string logUrl = innerUrl != null ? innerUrl.ToString() : $"/deployments/{id}/log";
            var json = await InvokeRequest<List<Dictionary<string, string>>>(client,
                HttpMethod.Get, logUrl);

            var logs = json.Where(dict => DateTime.Parse(dict["log_time"]) > lastUpdate || dict["details_url"] != null);
            DateTime currentLogDatetime = lastUpdate;
            foreach (var log in logs)
            {
                // Filter out details_url log
                if (DateTime.Parse(log["log_time"]) > lastUpdate)
                {
                    ColoredConsole.WriteLine(log["message"]);
                }

                // Recursively log details_url from scm/api/deployments/xxx/log endpoint
                if (log["details_url"] != null && Uri.TryCreate(log["details_url"], UriKind.Absolute, out Uri detailsUrl))
                {
                    DateTime innerLogDatetime = await DisplayDeploymentLog(client, functionApp, id, currentLogDatetime, detailsUrl);
                    currentLogDatetime = innerLogDatetime > currentLogDatetime ? innerLogDatetime : currentLogDatetime;
                }
            }

            if (logs.LastOrDefault() != null)
            {
                DateTime lastLogDatetime = DateTime.Parse(logs.Last()["log_time"]);
                currentLogDatetime = lastLogDatetime > currentLogDatetime ? lastLogDatetime : currentLogDatetime;
            }
            return currentLogDatetime;
        }

        private static async Task<T> InvokeRequest<T>(HttpClient client, HttpMethod method, string url)
        {
            HttpResponseMessage response = null;
            await RetryHelper.Retry(async () =>
            {
                using (var request = new HttpRequestMessage(method, new Uri(url, UriKind.RelativeOrAbsolute)))
                {
                    response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                }
            }, 3);

            if (response != null)
            {
                string jsonString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(jsonString);
            } else
            {
                return default(T);
            }
        }

        private static DeployStatus ConvertToDeployementStatus(string statusString)
        {
            return Enum.Parse<DeployStatus>(statusString);
        }
    }
}
