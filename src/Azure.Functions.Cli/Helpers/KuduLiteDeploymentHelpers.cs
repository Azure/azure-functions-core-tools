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
        public static async Task<DeployStatus> WaitForServerSideBuild(HttpClient client, Site functionApp, string restrictedToken)
        {
            ColoredConsole.WriteLine("Server side build in progress, please wait");
            DeployStatus statusCode = DeployStatus.Pending;
            DateTime logLastUpdate = DateTime.MinValue;
            string id = null;

            while (string.IsNullOrEmpty(id))
            {
                id = await GetLatestDeploymentId(client, functionApp, restrictedToken);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            while (statusCode != DeployStatus.Success && statusCode != DeployStatus.Failed)
            {
                statusCode = await GetDeploymentStatusById(client, functionApp, restrictedToken, id);
                logLastUpdate = await DisplayDeploymentLog(client, functionApp, restrictedToken, id, logLastUpdate);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            return statusCode;
        }

        private static async Task<string> GetLatestDeploymentId(HttpClient client, Site functionApp, string restrictedToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/deployments", UriKind.Relative)))
            {
                request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
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
            using (var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"/deployments/{id}", UriKind.Relative)))
            {
                request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string jsonString = await response.Content.ReadAsStringAsync();
                var json = JsonConvert.DeserializeObject<IDictionary<string, string>>(jsonString);
                return ConvertToDeployementStatus(json["status"]);
            }
        }

        private static async Task<DateTime> DisplayDeploymentLog(HttpClient client, Site functionApp, string restrictedToken, string id, DateTime lastUpdate)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri($"/deployments/{id}/log", UriKind.Relative)))
            {
                request.Headers.Add("x-ms-site-restricted-token", restrictedToken);
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
