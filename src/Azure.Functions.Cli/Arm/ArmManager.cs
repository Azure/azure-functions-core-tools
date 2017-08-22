using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Interfaces;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm
{
    internal partial class ArmManager : IArmManager
    {
        private readonly ISettings _settings;
        private readonly IArmTokenManager _tokenManager;
        private readonly ArmClient _client;

        public ArmManager(ISettings settings, IArmTokenManager tokenManager)
        {
            _settings = settings;
            _tokenManager = tokenManager;
            _client = new ArmClient(tokenManager, settings.CurrentTenant, retryCount: 3);
        }

        public async Task<IEnumerable<Site>> GetFunctionAppsAsync()
        {
            var subscription = await GetCurrentSubscriptionAsync();
            return await GetFunctionAppsAsync(subscription);
        }

        public async Task<Site> GetFunctionAppAsync(string name)
        {
            var functionApps = await GetFunctionAppsAsync();
            var functionApp = functionApps.FirstOrDefault(s => s.SiteName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (functionApp == null)
            {
                throw new ArmResourceNotFoundException($"Can't find app with name \"{name}\" in current subscription.");
            }
            else
            {
                return await LoadAsync(functionApp);
            }
        }

        public async Task<Site> EnsureScmTypeAsync(Site functionApp)
        {
            functionApp = await LoadSiteConfigAsync(functionApp);

            if (string.IsNullOrEmpty(functionApp.ScmType) ||
                functionApp.ScmType.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSiteConfigAsync(functionApp, new { properties = new { scmType = "LocalGit" } });
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            return functionApp;
        }

        public async Task<IEnumerable<TenantSubscriptionMap>> GetTenants()
        {
            var tenants = await _tokenManager.GetTenants();
            var tokenList = await tenants.Select(async t => new { Tenant = t, Token = await _tokenManager.GetToken(t) }).WhenAll();
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(Constants.ArmConstants.ArmResource);
                return await tokenList
                .Select(async t =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/subscriptions?api-version=2014-04-01");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t.Token);
                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var resString = await response.Content.ReadAsStringAsync();
                        var subscriptions = JsonConvert.DeserializeObject<ArmSubscriptionsArray>(resString);
                        return new TenantSubscriptionMap
                        {
                            TenantId = t.Tenant,
                            Subscriptions = subscriptions.Value
                        };
                    }
                    else
                    {
                        return null;
                    }
                })
                .WhenAll();
            }
        }

        private async Task<T> ArmHttpAsync<T>(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<T>();
        }

        private async Task ArmHttpAsync(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync()
        {
            var subscription = await GetCurrentSubscriptionAsync();
            return await GetStorageAccountsAsync(subscription);
        }

        public async Task<IEnumerable<ArmWrapper<object>>> getAzureResourceAsync(string resourceName)
        {
            var subscription = await GetCurrentSubscriptionAsync();
            return await GetResourcesByNameAsync(subscription, resourceName);
        }

        private async Task<IEnumerable<ArmWrapper<object>>> GetResourcesByNameAsync(Subscription subscription, string resourceName)
        {
            var armSubscriptionResourcesResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionResourceByName.Bind(new { subscriptionId = subscription.SubscriptionId, resourceName = resourceName }));
            armSubscriptionResourcesResponse.EnsureSuccessStatusCode();

            var resources = await armSubscriptionResourcesResponse.Content.ReadAsAsync<ArmArrayWrapper<object>>();
            return resources.Value;
        }

        public async Task<StorageAccount> GetStorageAccountsAsync(ArmWrapper<object> armWrapper)
        {
            var regex = new Regex("/subscriptions/(.*)/resourceGroups/(.*)/providers/Microsoft.Storage/storageAccounts/(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(armWrapper.Id);
            if (match.Success)
            {
                var storageAccount = new StorageAccount(match.Groups[1].ToString(), match.Groups[2].ToString(), match.Groups[3].ToString(), string.Empty);
                return await LoadAsync(storageAccount);
            }
            else
            {
                return null;
            }
        }
    }
}