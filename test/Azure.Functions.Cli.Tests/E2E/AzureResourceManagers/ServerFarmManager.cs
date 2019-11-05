using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers
{
    public class ServerFarmManager : MultiOsResourcesManager<string>
    {
        public async Task<HttpResponseMessage> Get(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/serverfarms/{name}?api-version=2019-08-01");
            return await ArmClient.HttpInvoke("GET", uri, AccessToken);
        }

        public async Task Create(
            string name,
            FunctionAppLocation location = FunctionAppLocation.WestUs2,
            FunctionAppSku sku = FunctionAppSku.Consumption,
            FunctionAppOs os = FunctionAppOs.Windows)
        {
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/serverfarms/{name}?api-version=2019-08-01");
            object payload = new
            {
                kind = os.GetServerFarmKindLabel(),
                location = location.ToRegion(),
                properties = sku.GetServerFarmProperties(os),
                sku = sku.GetServerFarmSku(os)
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
                throw new Exception($"Failed to create server farm {name}: ({statusCode}) {message}");
            }
        }

        public async Task Delete(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/serverfarms/{name}?api-version=2019-08-01");

            var response = await ArmClient.HttpInvoke("DELETE", uri, AccessToken);
            if (response.IsSuccessStatusCode)
            {
                RemoveFromResources(name, os);
            }
            else
            {
                string statusCode = response.StatusCode.ToString();
                string message = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to remove server farm {name}: ({statusCode}) {message}");
            }
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
