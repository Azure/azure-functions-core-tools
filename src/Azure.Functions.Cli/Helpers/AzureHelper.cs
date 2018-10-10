using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class AzureHelper
    {
        private static string _storageApiVersion = "2018-02-01";

        public static async Task<Site> GetFunctionApp(string name, string accessToken)
        {
            var subscriptions = await GetSubscriptions(accessToken);
            foreach (var subscription in subscriptions.value)
            {
                var functionApps = await ArmHttpAsync<ArmArrayWrapper<ArmGenericResource>>(
                HttpMethod.Get,
                ArmUriTemplates.SubscriptionResourceByNameAndType.Bind(new
                {
                    subscriptionId = subscription.subscriptionId,
                    resourceType = "Microsoft.Web/sites",
                    resourceName = name
                }),
                accessToken);

                // If we haven't found the functionapp, and there is a next page, keep going
                while (!functionApps.value.Any() && !string.IsNullOrEmpty(functionApps.nextLink))
                {
                    try
                    {
                        functionApps = await ArmHttpAsync<ArmArrayWrapper<ArmGenericResource>>(HttpMethod.Get, new Uri(functionApps.nextLink), accessToken);
                    }
                    catch (Exception)
                    {
                        // If we can't go to the next page for some reason, just move on for now
                        break;
                    }
                }

                if (functionApps.value.Any())
                {
                    var app = new Site(functionApps.value.First().id);
                    await LoadFunctionApp(app, accessToken);
                    return app;
                }
            }

            throw new ArmResourceNotFoundException($"Can't find app with name \"{name}\"");
        }

        internal static Task<IEnumerable<FunctionInfo>> GetFunctions(Site functionApp, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{functionApp.SiteId}/hostruntime/admin/functions?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return ArmHttpAsync<IEnumerable<FunctionInfo>>(HttpMethod.Get, url, accessToken);
        }

        internal static async Task<string> GetFunctionKey(string functionAdminUrl, string functionScmUri, string accessToken)
        {
            // If anything goes wrong anywhere, simply return null and let the caller take care of it.
            if (string.IsNullOrEmpty(functionAdminUrl) || string.IsNullOrEmpty(functionScmUri) || string.IsNullOrEmpty(accessToken))
            {
                return null;
            }
            var scmUrl = new Uri($"https://{functionScmUri}/api/functions/admin/token");
            var url = new Uri($"{functionAdminUrl}/keys");
            var key = "";
            try
            {
                var token = await ArmHttpAsync<string>(HttpMethod.Get, scmUrl, accessToken);
                var keysJson = await ArmHttpAsync<JToken>(HttpMethod.Get, url, token);
                key = (string)(keysJson["keys"] as JArray).First()["value"];
            }
            catch (Exception)
            {
                return null;
            }
            return key;
        }

        private static async Task<Site> LoadFunctionApp(Site site, string accessToken)
        {
            await new[]
            {
                LoadSiteObjectAsync(site, accessToken),
                LoadSitePublishingCredentialsAsync(site, accessToken),
                LoadSiteConfigAsync(site, accessToken),
                LoadAppSettings(site, accessToken),
                LoadAuthSettings(site, accessToken),
                LoadConnectionStrings(site, accessToken)
            }
            //.IgnoreFailures()
            .WhenAll();
            return site;
        }

        private async static Task<Site> LoadConnectionStrings(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/ConnectionStrings/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, AppServiceConnectionString>>>(HttpMethod.Post, url, accessToken);
            site.ConnectionStrings = armResponse.properties;
            return site;
        }

        private async static Task<Site> LoadAppSettings(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/AppSettings/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, string>>>(HttpMethod.Post, url, accessToken);
            site.AzureAppSettings = armResponse.properties;
            return site;
        }

        private async static Task<Site> LoadAuthSettings(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/AuthSettings/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, string>>>(HttpMethod.Post, url, accessToken);
            site.AzureAuthSettings = armResponse.properties;
            return site;
        }

        public static async Task<Site> LoadSitePublishingCredentialsAsync(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/PublishingCredentials/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return site.MergeWith(
                        await ArmHttpAsync<ArmWrapper<ArmWebsitePublishingCredentials>>(
                            HttpMethod.Post,
                            url,
                            accessToken),
                        t => t.properties
                    );
        }

        public static async Task<StorageAccount> GetStorageAccount(string storageAccountName, string accessToken)
        {
            var subscriptions = await GetSubscriptions(accessToken);
            foreach (var subscription in subscriptions.value)
            {
                var storageAccount =
                    await ArmHttpAsync<ArmArrayWrapper<ArmGenericResource>>(
                        HttpMethod.Get,
                        ArmUriTemplates.SubscriptionResourceByNameAndType.Bind(new
                        {
                            subscriptionId = subscription.subscriptionId,
                            resourceName = storageAccountName,
                            resourceType = "Microsoft.Storage/storageAccounts"
                        }),
                        accessToken);

                if (storageAccount.value.Any())
                {
                    return await GetStorageAccount(storageAccount.value.First(), accessToken);
                }
            }

            throw new ArmResourceNotFoundException($"Cannot find storage account with name {storageAccountName}");
        }

        private static async Task<StorageAccount> GetStorageAccount(ArmWrapper<ArmGenericResource> armWrapper, string accessToken)
        {
            try
            {
                var url = new Uri($"{ArmUriTemplates.ArmUrl}{armWrapper.id}/listKeys?api-version={_storageApiVersion}");
                var keys = await ArmHttpAsync<ArmStorageKeysArray>(HttpMethod.Post, url, accessToken);
                return new StorageAccount
                {
                    StorageAccountName = armWrapper.name,
                    StorageAccountKey = keys.keys.First().value
                };
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(e.ToString()));
                }

                throw new CliException($"Cannot get keys for storage account {armWrapper.name}. Make sure you have access to the storage account.");
            }
        }

        internal static Task<ArmSubscriptionsArray> GetSubscriptions(string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}/subscriptions?api-version={ArmUriTemplates.ArmApiVersion}");
            return ArmHttpAsync<ArmSubscriptionsArray>(
                HttpMethod.Get,
                url,
                accessToken);
        }

        public static async Task<Site> LoadSiteConfigAsync(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/web?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return site.MergeWith(
                  await ArmHttpAsync<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Get, url, accessToken),
                  t => t.properties
              );
        }

        public static Task<HttpResponseMessage> SyncTriggers(Site functionApp, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{functionApp.SiteId}/hostruntime/admin/host/synctriggers?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return ArmClient.HttpInvoke(HttpMethod.Post, url, accessToken);
        }

        public static async Task<Site> LoadSiteObjectAsync(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armSite = await ArmHttpAsync<ArmWrapper<ArmWebsite>>(HttpMethod.Get, url, accessToken);

            site.HostName = armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) == -1);
            site.ScmUri = armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) != -1);
            site.Location = armSite.location;
            site.Kind = armSite.kind;
            site.Sku = armSite.properties.sku;
            site.SiteName = armSite.name;
            return site;
        }

        private static async Task<T> ArmHttpAsync<T>(HttpMethod method, Uri uri, string accessToken, object payload = null)
        {
            var response = await ArmClient.HttpInvoke(method, uri, accessToken, payload, retryCount: 3);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<T>();
        }

        private static async Task ArmHttpAsync(HttpMethod method, Uri uri, string accessToken, object payload = null)
        {
            var response = await ArmClient.HttpInvoke(method, uri, accessToken, payload, retryCount: 3);
            response.EnsureSuccessStatusCode();
        }

        public static async Task<HttpResult<Dictionary<string, string>, string>> UpdateFunctionAppAppSettings(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/AppSettings?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var response = await ArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, new { properties = site.AzureAppSettings });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsAsync<ArmWrapper<Dictionary<string, string>>>();
                return new HttpResult<Dictionary<string, string>, string>(result.properties);
            }
            else
            {
                var result = await response.Content.ReadAsStringAsync();
                var parsedResult = JsonConvert.DeserializeObject<JObject>(result);
                var errorMessage = parsedResult["Message"].ToString();
                return string.IsNullOrEmpty(errorMessage)
                    ? new HttpResult<Dictionary<string, string>, string>(null, result)
                    : new HttpResult<Dictionary<string, string>, string>(null, errorMessage);
            }
        }

        public static async Task<HttpResult<Dictionary<string, string>, string>> UpdateFunctionAppAuthSettings(Site site, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/authsettings?api-version={_storageApiVersion}");
            var response = await ArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, new { properties = site.AzureAuthSettings });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsAsync<ArmWrapper<Dictionary<string, string>>>();
                return new HttpResult<Dictionary<string, string>, string>(result.properties);
            }
            else
            {
                var result = await response.Content.ReadAsStringAsync();
                var parsedResult = JsonConvert.DeserializeObject<JObject>(result);
                var errorMessage = parsedResult["Message"].ToString();
                return string.IsNullOrEmpty(errorMessage)
                    ? new HttpResult<Dictionary<string, string>, string>(null, result)
                    : new HttpResult<Dictionary<string, string>, string>(null, errorMessage);
            }
        }

        public static async Task PrintFunctionsInfo(Site functionApp, string accessToken, bool showKeys)
        {
            var functions = await GetFunctions(functionApp, accessToken);
            ColoredConsole.WriteLine(TitleColor($"Functions in {functionApp.SiteName}:"));
            foreach (var function in functions)
            {
                var trigger = function
                    .Config?["bindings"]
                    ?.FirstOrDefault(o => o["type"]?.ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                    ?["type"];

                trigger = trigger ?? "No Trigger Found";

                ColoredConsole.WriteLine($"    {function.Name} - [{VerboseColor(trigger.ToString())}]");
                if (!string.IsNullOrEmpty(function.InvokeUrlTemplate))
                {
                    // If there's a key available and the key is requested, add it to the url
                    var key = showKeys? await GetFunctionKey(function.Href.AbsoluteUri, functionApp.ScmUri, accessToken) : null;
                    if (!string.IsNullOrEmpty(key))
                    {
                        ColoredConsole.WriteLine($"        Invoke url: {VerboseColor($"{function.InvokeUrlTemplate}?code={key}")}");
                    }
                    else
                    {
                        ColoredConsole.WriteLine($"        Invoke url: {VerboseColor(function.InvokeUrlTemplate)}");
                    }
                }
                ColoredConsole.WriteLine();
            }
        }
    }
}