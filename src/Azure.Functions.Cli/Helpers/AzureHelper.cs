using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
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

        internal static async Task<string> GetFunctionKey(string functionName, string appId, string accessToken)
        {
            // If anything goes wrong anywhere, simply return null and let the caller take care of it.
            if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(accessToken))
            {
                return null;
            }
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{appId}/hostruntime/admin/functions/{functionName}/keys?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var key = string.Empty;
            try
            {
                var keysJson = await ArmHttpAsync<JToken>(HttpMethod.Get, url, accessToken);
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

        public static async Task<HttpResult<string, string>> UpdateWebSettings(Site site, Dictionary<string, string> webSettings, string accessToken)
        {
            var url = new Uri($"{ArmUriTemplates.ArmUrl}{site.SiteId}/config/web?api-version={ArmUriTemplates.WebsitesApiVersion}");
            var response = await ArmClient.HttpInvoke(HttpMethod.Put, url, accessToken, new { properties = webSettings });
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
                    var key = showKeys? await GetFunctionKey(function.Name, functionApp.SiteId, accessToken) : null;
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

        private static void EnsureAzureCli()
        {
            if (!CommandChecker.CommandExists("az"))
            {
                throw new CliException("Unable to connect to Azure. Make sure you have the `az` CLI installed and logged in and try again");
            }
        }

        private static async Task<string> RunAzCliCommand(string cliArgs)
        {
            EnsureAzureCli();
            var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new Executable("cmd", $"/c az {cliArgs}")
                : new Executable("az", $"{cliArgs}");
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
            if (exitCode == 0)
            {
                return stdout.ToString().Trim(' ', '\n', '\r', '"');
            }
            throw new CliException($"Error running az CLI command 'az {cliArgs}'. Error: {stderr.ToString().Trim(' ', '\n', '\r')}");
        }

        public static async Task<bool> CheckIfResourceGroupAlreadyExists(string resoruceGroup)
        {
            var azOutput = await RunAzCliCommand($"group exists --name {resoruceGroup} --output json");
            if (azOutput.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public static async Task<bool> IsStorageAccountNameTaken(string name)
        {
            var azOutput = await RunAzCliCommand($"storage account check-name --name {name} --output json");
            try
            {
                var available = JsonConvert.DeserializeObject<JObject>(azOutput)["nameAvailable"].ToString();
                if (available.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                throw new CliException($"Error trying to get the name availability of the Azure Storage Account name {name}");
            }
            return true;
        }

        public static async Task<bool> CheckIfFunctionAppAlreadyExists(string name, string accessToken)
        {
            try
            {
                var functionApp = await GetFunctionApp(name, accessToken);
                return true;
            }
            catch (ArmResourceNotFoundException)
            {
                // If we didn't find the resource
                return false;
            }
        }

        public static async Task CreateResourceGroup(string location, string name)
        {
            var azOutput = await RunAzCliCommand($"group create -l {location} -n {name}");
            try
            {
                var properties = JsonConvert.DeserializeObject<JObject>(azOutput)["properties"].ToString();
                var provisioningState = JsonConvert.DeserializeObject<JObject>(properties)["provisioningState"].ToString();
                if (!provisioningState.Contains("Succeeded"))
                {
                    throw new CliException($"Trying to create a Resource Group failed. az cli returned a status of {provisioningState} instead of 'Succeeded'");
                }
            }
            catch
            {
                throw new CliException($"Error trying to get the success status while creating a new Resource Group");
            }
        }

        public static async Task CreateAzureStorage(string name, string resourceGroup, string sku= "Standard_LRS")
        {
            var azOutput = await RunAzCliCommand($"storage account create --name {name} --resource-group {resourceGroup} --sku {sku}");
            try
            {
                var provisioningState = JsonConvert.DeserializeObject<JObject>(azOutput)["provisioningState"].ToString();
                if (!provisioningState.Contains("Succeeded"))
                {
                    throw new CliException($"Trying to create a Storage Account failed. az cli returned a status of {provisioningState} instead of 'Succeeded'");
                }
            }
            catch
            {
                throw new CliException($"Error trying to get the success status while creating a new Storage Account");
            }
        }

        public static async Task CreateAzureFunction(string resourceGroup, string storageAccount, string name, string os, string runtime, string location)
        {
            var azOutput = await RunAzCliCommand($"functionapp create --resource-group {resourceGroup} -s {storageAccount} --name {name} --os-type {os} --runtime {runtime} -c {location}");
            var available = "";
            try
            {
                available = JsonConvert.DeserializeObject<JObject>(azOutput)["availabilityState"].ToString();
                if (available.Equals("normal", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (Exception)
            {
                throw new CliException("Error trying to get the availabilityState of the created function app");
            }
            throw new CliException($"The availabilityState returned a status of {available} instead of Normal");
        }
    }
}