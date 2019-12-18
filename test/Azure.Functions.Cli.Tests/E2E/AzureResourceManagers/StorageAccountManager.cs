using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers
{
    public class StorageAccountManager : MultiOsResourcesManager<string>
    {
        private HashSet<string> _storageAccounts;

        public StorageAccountManager() : base()
        {
            _storageAccounts = new HashSet<string>();
        }

        public async Task<HttpResponseMessage> Get(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{name}?api-version=2019-06-01");
            return await ArmClient.HttpInvoke("GET", uri, AccessToken);
        }

        public async Task<ListKeysResponse> ListKeys(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{name}/listkeys?api-version=2019-06-01");
            var response = await ArmClient.HttpInvoke("POST", uri, AccessToken);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ListKeysResponse>(json);
            }
            else
            {
                string statusCode = response.StatusCode.ToString();
                string message = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to list keys for storage account {name}: ({statusCode}) {message}");
            }
        }

        public async Task Create(
            string name,
            FunctionAppLocation location = FunctionAppLocation.WestUs2,
            FunctionAppOs os = FunctionAppOs.Windows)
        {
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{name}?api-version=2019-06-01");
            object payload = new
            {
                kind = "StorageV2",
                location = location.ToRegion(),
                sku = new
                {
                    name = "Standard_LRS"
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
                throw new Exception($"Failed to create storage account {name}: ({statusCode}) {message}");
            }
        }

        public async Task Delete(
            string name)
        {
            FunctionAppOs os = GetOsFromResourceLabel(name);
            string resourceGroup = GetResourceGroupName(os);
            Uri uri = new Uri($"{ManagementURL}subscriptions/{SubscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{name}?api-version=2019-06-01");
            var response = await ArmClient.HttpInvoke("DELETE", uri, AccessToken);
            if (response.IsSuccessStatusCode)
            {
                RemoveFromResources(name, os);
            }
            else
            {
                string statusCode = response.StatusCode.ToString();
                string message = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to remove storage account {name}: ({statusCode}) {message}");
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

        public async Task<ListKeysResponse> WaitUntilListKeys(
            string name,
            int numOfRetries = 5,
            int retryIntervalSec = 10)
        {
            ListKeysResponse result = null;
            await RetryHelper.Retry(async () =>
            {
                result = await ListKeys(name);
            }, retryCount: numOfRetries, retryDelay: TimeSpan.FromSeconds(retryIntervalSec));
            return result;
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
