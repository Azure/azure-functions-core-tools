using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ContainerApps.Models;
using Azure.Functions.Cli.Extensions;
using Colors.Net;
using Microsoft.Azure.AppService.Middleware;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Http.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class AzureHelper
    {
        private static string _storageApiVersion = "2018-02-01";

        internal static async Task<Site> GetFunctionApp(string name, string accessToken, string managementURL, string slot = null, string defaultSubscription = null, IEnumerable<ArmSubscription> allSubs = null, Func<Site, string, string, Task<Site>> loadFunction = null)
        {
            IEnumerable<string> allSubscriptionIds;
            if (defaultSubscription != null)
            {
                allSubscriptionIds = new string[] { defaultSubscription };
            }
            else
            {
                var subscriptions = allSubs ?? await GetSubscriptions(accessToken, managementURL);
                allSubscriptionIds = subscriptions.Select(sub => sub.subscriptionId);
            }

            var result = await TryGetFunctionAppFromArg(name, allSubscriptionIds, accessToken, managementURL, slot, loadFunction);
            if (result != null)
            {
                return result;
            }

            var errorMsg = slot == null
                ? $"Can't find app with name \"{name}\""
                : $"Can't find the function app slot with name \"{slot}\"";

            throw new ArmResourceNotFoundException(errorMsg);
        }

        private static async Task<Site> TryGetFunctionAppFromArg(string name, IEnumerable<string> subscriptions, string accessToken, string managementURL, string slot = null, Func<Site, string, string, Task<Site>> loadFunctionAppFunc = null)
        {
            var resourceType = "Microsoft.Web/sites";
            var resourceName = name;
            if (slot != null)
            {
                resourceType = "Microsoft.Web/sites/slots";
                resourceName = $"{name}/{slot}";
            }
            var query = $"where type =~ '{resourceType}' and name =~ '{resourceName}' | project id";

            try
            {
                string siteId = await GetResourceIDFromArg(subscriptions, query, accessToken, managementURL);

                var app = new Site(siteId);

                // The implementation of the load function is different for certain function apps like function apps based on Container Apps. 
                // If the implementation is not provided in the param then default one is used.
                if (loadFunctionAppFunc != null)
                {
                    await loadFunctionAppFunc(app, accessToken, managementURL);
                }
                else
                {
                    await LoadFunctionApp(app, accessToken, managementURL);
                }
                
                return app;
            }
            catch { }
            return null;
        }

        internal static async Task<string> GetApplicationInsightIDFromIKey(string iKey, string accessToken, string managementURL, IEnumerable<ArmSubscription> allSubs = null)
        {
            var allArmSubscriptions = allSubs ?? await GetSubscriptions(accessToken, managementURL);
            var allSubscriptionIds = allArmSubscriptions.Select(sub => sub.subscriptionId);

            var query = $"where type =~ 'Microsoft.Insights/components' and properties.InstrumentationKey == '{iKey}' | project id";

            try
            {
                return await GetResourceIDFromArg(allSubscriptionIds, query, accessToken, managementURL);
            }
            catch
            {
                throw new CliException("Could not find the Application Insights using the configured Instrumentation Key.");
            }
        }

        internal static async Task<string> GetResourceIDFromArg(IEnumerable<string> subIds, string query, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}/{ArmUriTemplates.ArgUri}?api-version={ArmUriTemplates.ArgApiVersion}");
            var bodyObject = new
            {
                subscriptions = subIds,
                query
            };

            var response = await CliArmClient.HttpInvoke(HttpMethod.Post, url, accessToken, objectPayload: bodyObject);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            var argResponse = JsonConvert.DeserializeObject<ArgResponse>(result);

            // we need the first item of the first row
            return argResponse.Data?.Rows?.FirstOrDefault()?.FirstOrDefault()
                ?? throw new CliException("Error finding the Azure Resource information.");
        }


        internal static async Task<bool> IsBasicAuthAllowedForSCM(Site functionApp, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{functionApp.SiteId}/basicPublishingCredentialsPolicies/scm?api-version={ArmUriTemplates.BasicAuthCheckApiVersion}");
            
            var response = await CliArmClient.HttpInvoke(HttpMethod.Get, url, accessToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            var basicAuthResponse = JsonConvert.DeserializeObject<BasicAuthCheckResponse>(result);

            return basicAuthResponse.Properties.Allow;
        }

        internal static ArmResourceId ParseResourceId(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentNullException(nameof(resourceId));
            }

            // Example Resource Id: /subscriptions/0000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/microsoft.insights/components/my-appinsights
            // This format is very unlikely to change, and it is ok to hold a dependency on it.
            var resourceElements = resourceId.Split('/');
            if (resourceElements.Length != 9)
            {
                throw new ArgumentException($"{nameof(resourceId)} ({resourceId}) is an invalid resource Id. Expected 9 resource elements. Found {resourceElements.Length}");
            }

            return new ArmResourceId
            {
                Subscription = resourceElements[2],
                ResourceGroup = resourceElements[4],
                Provider = resourceElements[6],
                Name = resourceElements[8]
            };
        }

        internal static Task<ArmArrayWrapper<FunctionInfo>> GetFunctions(Site functionApp, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{functionApp.SiteId}/functions?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return ArmHttpAsync<ArmArrayWrapper<FunctionInfo>>(HttpMethod.Get, url, accessToken);
        }

        internal static async Task<string> GetFunctionKey(string functionName, string appId, string accessToken, string managementURL)
        {
            // If anything goes wrong anywhere, simply return null and let the caller take care of it.
            if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(accessToken))
            {
                return null;
            }
            var url = new Uri($"{managementURL}{appId}/functions/{functionName}/listKeys?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var key = string.Empty;
            try
            {
                var keysJson = await ArmHttpAsync<JToken>(HttpMethod.Post, url, accessToken);
                key = keysJson.Select(k => k.Values()).SelectMany(i => i).FirstOrDefault()?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
            return key;
        }

        internal static async Task<string> GetMasterKeyAsync(string appId, string accessToken, string managementURL)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            var uri = $"{managementURL}{appId}/host/default/listKeys?api-version={ArmUriTemplates.WebsitesApiVersion}";
            var key = string.Empty;
            try
            {
                var keysJson = await ArmHttpAsync<JToken>(HttpMethod.Post, new Uri(uri), accessToken);
                key = keysJson["masterKey"]?.ToString();
                if (StaticSettings.IsDebug && string.IsNullOrEmpty(key))
                {
                    ColoredConsole.WriteLine($"Can not find masterKey for {appId}. URI: {uri}");
                    return null;
                }
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine($"Can not get masterKey for {appId}. URI: {uri} : {e.Message} : {e.StackTrace}");
                }
                return null;
            }
            return key;
        }
        

        internal static async Task<Site> LoadFunctionAppInContainerApp(Site site, string accessToken, string managementURL)
        {
            await new[]
            {
                LoadSiteObjectAsync(site, accessToken, managementURL),
                LoadSiteConfigAsync(site, accessToken, managementURL),
                LoadAppSettings(site, accessToken, managementURL),
            }
            .WhenAll();
            return site;
        }

        private static async Task<Site> LoadFunctionApp(Site site, string accessToken, string managementURL)
        {
            await new[]
            {
                LoadSiteObjectAsync(site, accessToken, managementURL),
                LoadSitePublishingCredentialsAsync(site, accessToken, managementURL),
                LoadSiteConfigAsync(site, accessToken, managementURL),
                LoadAppSettings(site, accessToken, managementURL),
                LoadConnectionStrings(site, accessToken, managementURL)
            }
            //.IgnoreFailures()
            .WhenAll();
            return site;
        }

        private async static Task<Site> LoadConnectionStrings(Site site, string accessToken, string managementURL)
        {
            try
            {
                var url = new Uri($"{managementURL}{site.SiteId}/config/ConnectionStrings/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
                var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, AppServiceConnectionString>>>(HttpMethod.Post, url, accessToken);
                site.ConnectionStrings = armResponse.properties;
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(e);
                }
            }
            return site;
        }

        private async static Task<Site> LoadAppSettings(Site site, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}/config/AppSettings/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, string>>>(HttpMethod.Post, url, accessToken);
            site.AzureAppSettings = armResponse.properties;
            return site;
        }

        public static async Task<Site> LoadSitePublishingCredentialsAsync(Site site, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}/config/PublishingCredentials/list?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return site.MergeWith(
                        await ArmHttpAsync<ArmWrapper<ArmWebsitePublishingCredentials>>(
                            HttpMethod.Post,
                            url,
                            accessToken),
                        t => t.properties
                    );
        }

        public static async Task<StorageAccount> GetStorageAccount(string storageAccountName, string accessToken, string managementURL)
        {
            var subscriptions = await GetSubscriptions(accessToken, managementURL);
            foreach (var subscription in subscriptions)
            {
                var storageAccount =
                    await ArmHttpAsync<ArmArrayWrapper<ArmGenericResource>>(
                        HttpMethod.Get,
                        ArmUriTemplates.SubscriptionResourceByNameAndType.Bind(managementURL, new
                        {
                            subscriptionId = subscription.subscriptionId,
                            resourceName = storageAccountName,
                            resourceType = "Microsoft.Storage/storageAccounts"
                        }),
                        accessToken);

                if (storageAccount.value.Any())
                {
                    return await GetStorageAccount(storageAccount.value.First(), accessToken, managementURL);
                }
            }

            throw new ArmResourceNotFoundException($"Cannot find storage account with name {storageAccountName}");
        }

        private static async Task<StorageAccount> GetStorageAccount(ArmWrapper<ArmGenericResource> armWrapper, string accessToken, string managementURL)
        {
            try
            {
                var url = new Uri($"{managementURL}{armWrapper.id}/listKeys?api-version={_storageApiVersion}");
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

        internal static async Task<IEnumerable<ArmSubscription>> GetSubscriptions(string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}/subscriptions?api-version={ArmUriTemplates.ArmApiVersion}");
            var allSubs = new List<ArmSubscription>();

            var armSubResponse = await ArmHttpAsync<ArmSubscriptionsArray>(HttpMethod.Get, url, accessToken);
            allSubs.AddRange(armSubResponse.value);

            while (armSubResponse.nextLink != null)
            {
                armSubResponse = await ArmHttpAsync<ArmSubscriptionsArray>(HttpMethod.Get, new Uri(armSubResponse.nextLink), accessToken);
                allSubs.AddRange(armSubResponse.value);
            }

            return allSubs;
        }

        public static async Task<Site> LoadSiteConfigAsync(Site site, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}/config/web?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return site.MergeWith(
                  await ArmHttpAsync<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Get, url, accessToken),
                  t => t.properties
              );
        }

        public static Task<HttpResponseMessage> SyncTriggers(Site functionApp, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{functionApp.SiteId}/host/default/sync?api-version={ArmUriTemplates.WebsitesApiVersion}");
            return CliArmClient.HttpInvoke(HttpMethod.Post, url, accessToken);
        }

        internal static async Task CheckFunctionHostStatusForFlex(Site functionApp, string accessToken, string managementURL,
            HttpMessageHandler messageHandler = null)
        {
            ColoredConsole.Write("Checking the app health...");
            var masterKey = await GetMasterKeyAsync(functionApp.SiteId, accessToken, managementURL);

            if (masterKey is null)
            {
                throw new CliException($"The masterKey is null. hostname: {functionApp.HostName}.");
            }

            HttpMessageHandler handler = messageHandler ?? new HttpClientHandler();
            if (StaticSettings.IsDebug)
            {
                handler = new LoggingHandler(handler);
            }

            var functionAppReadyClient = new HttpClient(handler);
            const string jsonContentType = "application/json";
            functionAppReadyClient.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
            functionAppReadyClient.DefaultRequestHeaders.Add("Accept", jsonContentType);
            
            await RetryHelper.Retry(async () =>
            {
                functionAppReadyClient.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());
                var uri = new Uri($"https://{functionApp.HostName}/admin/host/status?code={masterKey}");
                var request = new HttpRequestMessage()
                {
                    RequestUri = uri,
                    Method = HttpMethod.Get
                };

                var response = await functionAppReadyClient.SendAsync(request);
                ColoredConsole.Write(".");

                if (response.IsSuccessStatusCode)
                {
                    ColoredConsole.WriteLine(" done");
                }
                else
                {
                    throw new CliException($"The host didn't return success status. Returned: {response.StatusCode}");
                }

            }, 15, TimeSpan.FromSeconds(3));
        }

        public static async Task<Site> LoadSiteObjectAsync(Site site, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var armSite = await ArmHttpAsync<ArmWrapper<ArmWebsite>>(HttpMethod.Get, url, accessToken);

            site.HostName = armSite.properties.enabledHostNames?.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) == -1);
            site.ScmUri = armSite.properties.enabledHostNames?.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) != -1);
            site.Location = armSite.location;
            site.Kind = armSite.kind;
            site.Sku = armSite.properties.sku;
            site.SiteName = armSite.name;
            return site;
        }

        private static async Task<T> ArmHttpAsync<T>(HttpMethod method, Uri uri, string accessToken, object payload = null)
        {
            var response = await CliArmClient.HttpInvoke(method, uri, accessToken, payload, retryCount: 3);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<T>();
        }

        private static async Task ArmHttpAsync(HttpMethod method, Uri uri, string accessToken, object payload = null)
        {
            var response = await CliArmClient.HttpInvoke(method, uri, accessToken, payload, retryCount: 3);
            response.EnsureSuccessStatusCode();
        }

        public static async Task<HttpResult<string, string>> UpdateWebSettings(Site site, Dictionary<string, string> webSettings, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}/config/web?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var response = await CliArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, new { properties = webSettings });
            if (response.IsSuccessStatusCode)
            {
                // Simply reading it as a string because we do not care about the result content particularly
                var result = await response.Content.ReadAsStringAsync();
                return new HttpResult<string, string>(result);
            }
            else
            {
                var result = await response.Content.ReadAsStringAsync();
                var parsedResult = JsonConvert.DeserializeObject<JObject>(result);
                var errorMessage = parsedResult["Message"].ToString();
                return string.IsNullOrEmpty(errorMessage)
                    ? new HttpResult<string, string>(null, result)
                    : new HttpResult<string, string>(null, errorMessage);
            }
        }

        public static async Task<HttpResult<Dictionary<string, string>, string>> UpdateFunctionAppAppSettings(Site site, string accessToken, string managementURL)
        {
            var url = new Uri($"{managementURL}{site.SiteId}/config/AppSettings?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var response = await CliArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, new { properties = site.AzureAppSettings });
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

        internal static async Task WaitUntilFunctionAppReadyAsync(Site functionApp, string accessToken, string managementURL,
            HttpMessageHandler messageHandler = null)
        {
            var masterKey = await GetMasterKeyAsync(functionApp.SiteId, accessToken, managementURL);

            if (masterKey is null)
            {
                throw new CliException($"The masterKey is null. hostname: {functionApp.HostName}.");
            }

            HttpMessageHandler handler = messageHandler ?? new HttpClientHandler();
            if (StaticSettings.IsDebug)
            {
                handler = new LoggingHandler(handler);
            }

            var functionAppReadyClient = new HttpClient(handler);
            const string jsonContentType = "application/json";
            functionAppReadyClient.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
            functionAppReadyClient.DefaultRequestHeaders.Add("Accept", jsonContentType);
            

            await RetryHelper.Retry(async () =>
                  {
                      functionAppReadyClient.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());
                      var uri = new Uri($"https://{functionApp.HostName}/admin/host/status?code={masterKey}");
                      var request = new HttpRequestMessage()
                      {
                          RequestUri = uri,
                          Method = HttpMethod.Get
                      };

                      var response = await functionAppReadyClient.SendAsync(request);
                      ColoredConsole.Write(".");

                      if (response.IsSuccessStatusCode)
                      {
                          var json = await response.Content.ReadAsAsync<JToken>();
                          var processUpTime = json["processUptime"]?.ToObject<int>() ?? 0;
                          // Wait for 30 sec after Restart is happening.
                          // We assume that we need to wait until the restart happens if the ProcessUpTime is greater than 45sec.
                          if (processUpTime >= Constants.DefaultGetFunctionReadinessTime &&
                              processUpTime < Constants.DefaultRestartedWorkerProcessUptimeWithin)
                          {
                              ColoredConsole.WriteLine(" done");
                              return;
                          }
                          else
                          {
                              throw new Exception($"GetFunctions is not ready. Hostname: {functionApp.HostName}");
                          }
                      }
                      else
                      {
                          throw new Exception($"Status check returns unsuccessful status. Hostname: {functionApp.HostName}");
                      }
                  }, 18, TimeSpan.FromSeconds(3)
            );
        }
        public static async Task<string> CreateFunctionAppOnContainerService(string accessToken, string managementURL, string subscriptionId, string resourceGroup, ContainerAppsFunctionPayload payload)
        {
            string hostName = string.Empty;
            var url = new Uri($"{managementURL}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{payload.Name}?api-version={ArmUriTemplates.FunctionAppOnContainerAppsApiVersion}");
            ColoredConsole.WriteLine(Constants.FunctionAppDeploymentToContainerAppsMessage);
            var response = await CliArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, payload);
            if (!response.IsSuccessStatusCode)
            {
                string errorMessage;
                try
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var errorResponse = JsonConvert.DeserializeObject<ContainerAppsFunctionDeployResponse>(content);
                    errorMessage = $"{Constants.FunctionAppFailedToDeployOnContainerAppsMessage} {(!string.IsNullOrWhiteSpace(errorResponse?.Message)? $"Error: {errorResponse.Message}" : string.Empty )}";
                }
                catch (Exception)
                {
                    errorMessage = Constants.FunctionAppFailedToDeployOnContainerAppsMessage;
                }

                throw new CliException(errorMessage);
            }
            
            var statusUrlHeader = response.Headers.GetValues("location").FirstOrDefault();
            if (string.IsNullOrEmpty(statusUrlHeader))
            {
                throw new CliException(Constants.FunctionAppDeploymentToContainerAppsFailedMessage);
            }

            ColoredConsole.Write(Constants.FunctionAppDeploymentToContainerAppsStatusMessage);
            var statusUrl = new Uri(statusUrlHeader);
            int maxRetries = 12; // 12 * 5 seconds = 1 minute
            for (int retry = 1; retry <= maxRetries; retry++)
            {
                var getResponse = await CliArmClient.HttpInvoke(HttpMethod.Get, statusUrl, accessToken, payload);
                if (getResponse.StatusCode != System.Net.HttpStatusCode.Accepted)
                {
                    getResponse.EnsureSuccessStatusCode();
                    var content = await getResponse.Content.ReadAsStringAsync();
                    var deployResponse = JsonConvert.DeserializeObject<ContainerAppFunctonCreateResponse>(content);
                    hostName = deployResponse?.Properties?.DefaultHostName;
                    break;
                }

                if (retry == maxRetries)
                {
                    throw new CliException("The creation of the function app could not be verified.");
                }

                // Waiting for 5 seconds before another request
                await Task.Delay(TimeSpan.FromSeconds(5));
                ColoredConsole.Write(".");
            }

            ColoredConsole.Write(Environment.NewLine);
            ColoredConsole.WriteLine($"The functon app \"{payload.Name}\" was successfully deployed.");
            return hostName;
        }

        public static async Task PrintFunctionsInfo(Site functionApp, string accessToken, string managementURL, bool showKeys)
        {
            if (functionApp.IsKubeApp)
            {
                ColoredConsole.Write("Waiting for the functions host up and running ");
                await WaitUntilFunctionAppReadyAsync(functionApp, accessToken, managementURL);
            }

            ArmArrayWrapper<FunctionInfo> functions = await GetFunctions(functionApp, accessToken, managementURL);
            functions = await GetFunctions(functionApp, accessToken, managementURL);

            ColoredConsole.WriteLine(TitleColor($"Functions in {functionApp.SiteName}:"));
            
            if (functionApp.IsKubeApp && !functions.value.Any())
            {
                ColoredConsole.WriteLine(WarningColor(
                    $"The deployment succeeded. Waiting for {Constants.DefaultRestartedWorkerProcessUptimeWithin / 1000} sec to fetch the list of functions deployed in the function app."));
            }
            foreach (var function in functions.value.Select(v => v.properties))
            {
                var trigger = function
                    .Config?["bindings"]
                    ?.FirstOrDefault(o => o["type"]?.ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                    ?["type"];

                trigger = trigger ?? "No Trigger Found";
                var showFunctionKey = showKeys;

                var authLevel = function
                    .Config?["bindings"]
                    ?.FirstOrDefault(o => !string.IsNullOrEmpty(o["authLevel"]?.ToString()))
                    ?["authLevel"];

                if (authLevel != null && authLevel.ToString().Equals("anonymous", StringComparison.OrdinalIgnoreCase))
                {
                    showFunctionKey = false;
                }

                ColoredConsole.WriteLine($"    {function.Name} - [{VerboseColor(trigger.ToString())}]");
                if (!string.IsNullOrEmpty(function.InvokeUrlTemplate))
                {
                    var invokeUrlUrlTemplate = function.InvokeUrlTemplate;
                    if (!string.IsNullOrEmpty(invokeUrlUrlTemplate) && !invokeUrlUrlTemplate.Contains(functionApp.HostName))
                    {
                        invokeUrlUrlTemplate = invokeUrlUrlTemplate.Replace($"{functionApp.SiteName}.azurewebsites.net", $"{functionApp.HostName}");
                    }
                    // If there's a key available and the key is requested, add it to the url
                    var key = showFunctionKey ? await GetFunctionKey(function.Name, functionApp.SiteId, accessToken, managementURL) : null;
                    if (!string.IsNullOrEmpty(key))
                    {
                        ColoredConsole.WriteLine($"        Invoke url: {VerboseColor($"{invokeUrlUrlTemplate}?code={key}")}");
                    }
                    else
                    {
                        ColoredConsole.WriteLine($"        Invoke url: {VerboseColor(invokeUrlUrlTemplate)}");
                    }
                }
                ColoredConsole.WriteLine();
            }
        }

        public static async Task<(string, string)> GetManagedEnvironmentInfo(string accessToken, string managementURL, string subscriptionId, string resourceGroup, string name)
        {
            var url = new Uri($"{managementURL}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.App/managedEnvironments/{name}?api-version={ArmUriTemplates.ManagedEnvironmentApiVersion}");
            var response = await CliArmClient.HttpInvoke(HttpMethod.Get, url, accessToken);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var managedEnvironment = JsonConvert.DeserializeObject<ManagedEnvironementGetResponse>(content);
                return (managedEnvironment?.Id, managedEnvironment?.Location);
            }
            else
            {
                return (null, null);
            }
        }
    }
}
