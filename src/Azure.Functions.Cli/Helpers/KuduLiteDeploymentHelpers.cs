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
        private static string CachedARMRestrictedToken = string.Empty;
        private static DateTime TokenLastUpdate = DateTime.MinValue;

        public static async Task<string> GetRestrictedToken(Site functionApp, string accessToken, string managementUrl)
        {
            DateTime expiry = TokenLastUpdate + TimeSpan.FromMinutes(Constants.KuduLiteDeploymentConstants.ArmTokenExpiryMinutes);
            if (DateTime.UtcNow > expiry || string.IsNullOrEmpty(CachedARMRestrictedToken))
            {
                try
                {
                    CachedARMRestrictedToken = await AzureHelper.GetSiteRestrictedToken(functionApp, accessToken, managementUrl);
                    TokenLastUpdate = DateTime.UtcNow;
                }
                catch
                {
                    // When host is restarting, we will suppress the error and use the old ARM token (still valid for 1 minute)
                }
            }
            return CachedARMRestrictedToken;
        }

        public static async Task<DeployStatus> WaitForConsumptionServerSideBuild(HttpClient client, Site functionApp, string accessToken, string managementUrl)
        {
            ColoredConsole.WriteLine("Remote build in progress, please wait...");
            DeployStatus statusCode = DeployStatus.Pending;
            DateTime logLastUpdate = DateTime.MinValue;
            string id = null;

            while (string.IsNullOrEmpty(id))
            {
                string restrictedToken = await GetRestrictedToken(functionApp, accessToken, managementUrl);
                id = await GetLatestDeploymentId(client, functionApp, restrictedToken);
                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed)
            {
                string restrictedToken = await GetRestrictedToken(functionApp, accessToken, managementUrl);
                statusCode = await GetDeploymentStatusById(client, functionApp, restrictedToken, id);
                logLastUpdate = await DisplayDeploymentLog(client, functionApp, restrictedToken, id, logLastUpdate);
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
                var json = await InvokeRequest<IDictionary<string, bool>>(client, HttpMethod.Get, "/api/isdeploying");
                bool isDeploying = json["value"];
                if (!isDeploying)
                {
                    string deploymentId = await GetLatestDeploymentId(client, functionApp, restrictedToken: null);
                    DeployStatus status = await GetDeploymentStatusById(client, functionApp, restrictedToken: null, id: deploymentId);
                    ColoredConsole.Write($"done{Environment.NewLine}");
                    return status;
                }
                ColoredConsole.Write(".");
                await Task.Delay(5000);
            }
        }

        private static async Task<string> GetLatestDeploymentId(HttpClient client, Site functionApp, string restrictedToken)
        {
            var json = await InvokeRequest<List<Dictionary<string, string>>>(client,
                HttpMethod.Get, "/deployments", restrictedToken);

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

        private static async Task<DeployStatus> GetDeploymentStatusById(HttpClient client, Site functionApp, string restrictedToken, string id)
        {
            var json = await InvokeRequest<Dictionary<string, string>>(client,
                HttpMethod.Get, $"/deployments/{id}", restrictedToken);

            if (json.TryGetValue("status", out string statusString))
            {
                return ConvertToDeployementStatus(json["status"]);
            }
            return DeployStatus.Failed;
        }

        private static async Task<DateTime> DisplayDeploymentLog(HttpClient client, Site functionApp, string restrictedToken, string id, DateTime lastUpdate)
        {
            var json = await InvokeRequest<List<Dictionary<string, string>>>(client,
                HttpMethod.Get, $"/deployments/{id}/log", restrictedToken);

            var logs = json.Where(dict => DateTime.Parse(dict["log_time"]) > lastUpdate);
            foreach (var log in logs)
            {
                ColoredConsole.WriteLine(log["message"]);
            }
            return logs.LastOrDefault() != null ? DateTime.Parse(logs.Last()["log_time"]) : lastUpdate;
        }

        private static async Task<T> InvokeRequest<T>(HttpClient client, HttpMethod method, string url, string restrictedToken=null)
        {
            HttpResponseMessage response = null;
            await RetryHelper.Retry(async () =>
            {
                using (var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)))
                {
                    if (!string.IsNullOrEmpty(restrictedToken))
                    {
                        request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
                    }

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
