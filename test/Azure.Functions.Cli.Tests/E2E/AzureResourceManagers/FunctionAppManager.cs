using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers
{
    public class FunctionAppManager : MultiOsResourcesManager<string>
    {
        public async Task<HttpResponseMessage> Get(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{name}?api-version=2018-11-01");
            return await ArmClient.HttpInvoke("GET", uri, AccessToken);
        }

        public async Task Create(
            string name,
            string storageAccountName,
            string storageAccountKey,
            string serverFarmName,
            FunctionAppLocation location = FunctionAppLocation.WestUs2,
            FunctionAppSku sku = FunctionAppSku.Consumption,
            FunctionAppOs os = FunctionAppOs.Windows,
            FunctionAppRuntime runtime = FunctionAppRuntime.DotNet)
        {
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{name}?api-version=2018-11-01");

            string storageAccountConnectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageAccountKey};EndpointSuffix=core.windows.net";

            List<Dictionary<string, string>> appSettings = new List<Dictionary<string, string>>();
            appSettings.Add(new Dictionary<string, string> { { "name", "FUNCTIONS_WORKER_RUNTIME" }, { "value", runtime.ToFunctionWorkerRuntime() } });
            appSettings.Add(new Dictionary<string, string> { { "name", "FUNCTIONS_EXTENSION_VERSION" }, { "value", "~2" } });
            appSettings.Add(new Dictionary<string, string> { { "name", "AzureWebJobsStorage" }, { "value", storageAccountConnectionString } });

            object payload = new
            {
                kind = os.GetFunctionAppKindLabel(),
                location = location.ToRegion(),
                properties = new
                {
                    siteConfig = new
                    {
                        appSettings = appSettings
                    },
                    serverFarmId = $"/subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/serverfarms/{serverFarmName}",
                    hostingEnvironment = "",
                    clientAffinityEnable = false
                }
            };

            var response = await ArmClient.HttpInvoke("PUT", uri, AccessToken, payload);
            if (response.IsSuccessStatusCode)
            {
                AddToResources(name, os);
            }
            else
            {
                string statusCode = response.StatusCode.ToString();
                string message = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create function app {name}: ({statusCode}) {message}");
            }
        }

        public async Task Delete(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);

            Uri uri = new Uri($"{ManagementURL}/subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{name}?api-version=2018-11-01");
            var response = await ArmClient.HttpInvoke("DELETE", uri, AccessToken);
            if (response.IsSuccessStatusCode)
            {
                RemoveFromResources(name, os);
            }
            else
            {
                string statusCode = response.StatusCode.ToString();
                string message = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to remove function app {name}: ({statusCode}) {message}");
            }
        }

        public async Task<bool> CheckIfSiteExistsFromArg(
            string name)
        {
            var url = new Uri($"{ManagementURL}/{ArmUriTemplates.ArgUri}?api-version={ArmUriTemplates.ArgApiVersion}");
            var payload = new
            {
                subscriptions = new[] { SubscriptionId },
                query = $"where type =~ \'Microsoft.Web/sites\' and name =~ \'{name}\'"
            };

            var response = await ArmClient.HttpInvoke(HttpMethod.Post, url, AccessToken, payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            var argResponse = JsonConvert.DeserializeObject<TestArgResponse>(result);
            return argResponse.Count > 0;
        }

        public bool Contains(
            string name)
        {
            return ContainsResource(name);
        }

        public async Task WaitUntilCreated(
            string name,
            int numOfRetries = 5,
            int retryIntervalSec = 10)
        {
            await RetryHelper.Retry(async () =>
            {
                HttpResponseMessage response = await Get(name);
                response.EnsureSuccessStatusCode();
            }, retryCount: numOfRetries, retryDelay: TimeSpan.FromSeconds(retryIntervalSec));
        }

        public async Task WaitUntilSiteAvailable(
            string name,
            int numOfRetries = 5,
            int retryIntervalSec = 10)
        {
            await RetryHelper.Retry(async () =>
            {
                bool exist = await CheckIfSiteExistsFromArg(name);
                if (!exist)
                {
                    throw new Exception($"Function app {name} does not exist in Azure Graph");
                }
            }, retryCount: numOfRetries, retryDelay: TimeSpan.FromSeconds(retryIntervalSec));
        }

        public async Task WaitUntilScmSiteAvailable(
            string name,
            int numOfRetries = 5,
            int retryIntervalSec = 10)
        {
            Uri uri = new Uri($"https://{name}.scm.azurewebsites.net");
            await RetryHelper.Retry(async () =>
            {
                HttpResponseMessage response = await ArmClient.HttpInvoke("GET", uri, AccessToken);
                response.EnsureSuccessStatusCode();
            }, retryCount: numOfRetries, retryDelay: TimeSpan.FromSeconds(retryIntervalSec));
        }

        public async Task WaitUntilDeleted(
            string name,
            int numOfRetries = 5,
            int retryIntervalSec = 10)
        {
            await RetryHelper.Retry(async () =>
            {
                await Delete(name);
            }, retryCount: numOfRetries, retryDelay: TimeSpan.FromSeconds(retryIntervalSec));
        }

        protected override void CleanUp()
        {
            List<Task> deletedTasks = new List<Task>();
            deletedTasks.AddRange(WindowsResources.Select(sa => WaitUntilDeleted(sa)));
            deletedTasks.AddRange(LinuxResources.Select(sa => WaitUntilDeleted(sa)));
            Task.WhenAll(deletedTasks).Wait();
        }
    }
}
