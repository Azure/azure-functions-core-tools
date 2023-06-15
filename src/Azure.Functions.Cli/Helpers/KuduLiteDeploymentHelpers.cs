using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Models;
using Colors.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal class KuduLiteDeploymentHelpers
    {
        public static async Task<Dictionary<string, string>> GetAppSettings(HttpClient client)
        {
            return await InvokeRequest<Dictionary<string, string>>(client, HttpMethod.Get, "/api/settings");
        }

        public static async Task<DeployStatus> WaitForRemoteBuild(HttpClient client, Site functionApp)
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

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed && statusCode != DeployStatus.Unknown)
            {
                try
                {
                    statusCode = await GetDeploymentStatusById(client, functionApp, id);
                    logLastUpdate = await DisplayDeploymentLog(client, functionApp, id, logLastUpdate);
                }
                catch (HttpRequestException)
                {
                    return DeployStatus.Unknown;
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            return statusCode;
        }


        public static async Task<DeployStatus> WaitForFlexDeployment(HttpClient client, Site functionApp)
        {
            ColoredConsole.WriteLine("Deployment in progress, please wait...");
            DeployStatus statusCode = DeployStatus.Pending;
            DateTime logLastUpdate = DateTime.MinValue;
            string id = null;

            while (string.IsNullOrEmpty(id))
            {
                id = await GetLatestDeploymentId(client, functionApp);
                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed && statusCode != DeployStatus.Unknown)
            {
                try
                {
                    statusCode = await GetDeploymentStatusById(client, functionApp, id);
                    if (!functionApp.IsFlex)
                    {
                        logLastUpdate = await DisplayDeploymentLog(client, functionApp, id, logLastUpdate);
                    }
                }
                catch (HttpRequestException)
                {
                    return DeployStatus.Unknown;
                }

                await Task.Delay(TimeSpan.FromSeconds(Constants.KuduLiteDeploymentConstants.StatusRefreshSeconds));
            }

            return statusCode;
        }


        private static async Task<string> GetLatestDeploymentId(HttpClient client, Site functionApp)
        {
            var deploymentUrl = "/deployments";
            if (functionApp.IsFlex)
            {
                deploymentUrl = "api/deployments";
            }
            
            var deployments = await InvokeRequest<List<DeploymentResponse>>(client, HttpMethod.Get, deploymentUrl);

            // Automatically ordered by received time
            var latestDeployment = deployments.First();
            DeployStatus? status = latestDeployment.Status;
            if (status == DeployStatus.Building || status == DeployStatus.Deploying
                || status == DeployStatus.Success || status == DeployStatus.Failed)
            {
                return latestDeployment.Id;
            }
            return null;
        }

        private static async Task<DeployStatus> GetDeploymentStatusById(HttpClient client, Site functionApp, string id)
        {
            var deploymentUrl = $"/deployments/{id}";
            if (functionApp.IsFlex)
            {
                deploymentUrl = $"/api/deployments/{id}";
            }

            var deploymentInfo = await InvokeRequest<DeploymentResponse>(client, HttpMethod.Get, deploymentUrl);
            DeployStatus? status = deploymentInfo.Status;
            if (status == null)
            {
                return DeployStatus.Unknown;
            }
            return status.Value;
        }

        private static async Task<DateTime> DisplayDeploymentLog(HttpClient client, Site functionApp, string id, DateTime lastUpdate, Uri innerUrl = null, StringBuilder innerLogger = null)
        {
            string logUrl = innerUrl != null ? innerUrl.ToString() : $"/deployments/{id}/log";
            StringBuilder sbLogger = innerLogger != null ? innerLogger : new StringBuilder();

            var deploymentLogs = await InvokeRequest<List<DeploymentLogResponse>>(client, HttpMethod.Get, logUrl);
            var newLogs = deploymentLogs.Where(deploymentLog => deploymentLog.LogTime > lastUpdate || !string.IsNullOrEmpty(deploymentLog.DetailsUrlString));
            DateTime currentLogDatetime = lastUpdate;

            foreach (var log in newLogs)
            {
                // Filter out details_url log
                if (log.LogTime > lastUpdate)
                {
                    sbLogger.AppendLine(log.Message);
                }

                // Recursively log details_url from scm/api/deployments/xxx/log endpoint
                if (!string.IsNullOrEmpty(log.DetailsUrlString) && Uri.TryCreate(log.DetailsUrlString, UriKind.Absolute, out Uri detailsUrl))
                {
                    DateTime innerLogDatetime = await DisplayDeploymentLog(client, functionApp, id, currentLogDatetime, detailsUrl, sbLogger);
                    currentLogDatetime = innerLogDatetime > currentLogDatetime ? innerLogDatetime : currentLogDatetime;
                }
            }

            if (newLogs.LastOrDefault() != null)
            {
                DateTime lastLogDatetime = newLogs.Last().LogTime;
                currentLogDatetime = lastLogDatetime > currentLogDatetime ? lastLogDatetime : currentLogDatetime;
            }

            // Report build status on the root level parser
            if (innerUrl == null && sbLogger.Length > 0)
            {
                ColoredConsole.Write(sbLogger.ToString());
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
    }
}
