using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Azure.Functions.Cli.Helpers
{
    internal class KuduLiteDeploymentHelpers
    {
        public static Uri GetZipDeployUri(bool? isAsync=null, string author=null)
        {
            var uriBuilder = new UriBuilder("api/zipdeploy");
            var uriQueryParams = HttpUtility.ParseQueryString(string.Empty);
            if (isAsync != null)
            {
                uriQueryParams["isAsync"] = isAsync.ToString();
            }
            if (author != null)
            {
                uriQueryParams["author"] = author;
            }

            uriBuilder.Query = uriQueryParams.ToString();
            return uriBuilder.Uri;
        }

        public static async Task<DeployStatus> WaitForServerSideBuild(HttpClient client, Site functionApp, string accessToken, string managementUrl)
        {
            ColoredConsole.WriteLine("Server side build in progress, please wait...");
            DeployStatus statusCode = DeployStatus.Pending;
            DateTime logLastUpdate = DateTime.MinValue;
            string id = null;

            while (string.IsNullOrEmpty(id))
            {
                string restrictedToken = await AzureHelper.GetSiteRestrictedToken(functionApp, accessToken, managementUrl);
                id = await GetLatestDeploymentId(client, functionApp, restrictedToken);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed)
            {
                string restrictedToken = await AzureHelper.GetSiteRestrictedToken(functionApp, accessToken, managementUrl);
                statusCode = await GetDeploymentStatusById(client, functionApp, restrictedToken, id);
                try
                {
                    logLastUpdate = await DisplayDeploymentLog(client, functionApp, restrictedToken, id, logLastUpdate);
                }
                catch (Exception)
                {
                    // Ignore Log Errors for now
                }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return statusCode;
        }

        public static async Task<DeployStatus> WaitForDedicatedBuildToComplete(HttpClient client, Site functionApp, string accessToken, string managementUrl)
        {
            // There is a tracked Locking issue in kudulite causing Race conditions, so we have to use this API
            // to gather deployment progress.
            ColoredConsole.Write("Server side build in progress, please wait");
            while (true)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/isdeploying", UriKind.Relative)))
                {
                    var response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var json = JsonConvert.DeserializeObject<IDictionary<string, bool>>(jsonString);

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
        }

        private static async Task<string> GetLatestDeploymentId(HttpClient client, Site functionApp, string restrictedToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/deployments", UriKind.Relative)))
            {
                if (restrictedToken != null)
                {
                    request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
                }
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonConvert.DeserializeObject<IList<IDictionary<string, string>>>(jsonString);

                // Automatically ordered by received time
                var latestDeployment = json.First();
                DeployStatus status = ConvertToDeployementStatus(latestDeployment["status"]);
                if (status == DeployStatus.Pending)
                {
                    return null;
                }
                else
                {
                    return latestDeployment["id"];
                }
            }
        }

        private static async Task<DeployStatus> GetDeploymentStatusById(HttpClient client, Site functionApp, string restrictedToken, string id)
        {
            return await RetryHelper.Retry<DeployStatus>(async () =>
            {
                using (var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"/deployments/{id}", UriKind.Relative)))
                {
                    if (restrictedToken != null)
                    {
                        request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
                    }
                    var response = await client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var json = JsonConvert.DeserializeObject<IDictionary<string, string>>(jsonString);
                    return ConvertToDeployementStatus(json["status"]);
                }
            }, 5, TimeSpan.FromSeconds(5));
        }

        private static async Task<DateTime> DisplayDeploymentLog(HttpClient client, Site functionApp, string restrictedToken, string id, DateTime lastUpdate)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"/deployments/{id}/log", UriKind.Relative)))
            {
                if (restrictedToken != null)
                {
                    request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
                }
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonConvert.DeserializeObject<IList<IDictionary<string, string>>>(jsonString);

                var logs = json.Where(dict => DateTime.Parse(dict["log_time"]) > lastUpdate);
                foreach (var log in logs)
                {
                    ColoredConsole.WriteLine(log["message"]);
                }
                return logs.LastOrDefault() != null ? DateTime.Parse(logs.Last()["log_time"]) : lastUpdate;
            }
        }

        private static DeployStatus ConvertToDeployementStatus(string statusString)
        {
            return Enum.Parse<DeployStatus>(statusString);
        } 

    }
}
