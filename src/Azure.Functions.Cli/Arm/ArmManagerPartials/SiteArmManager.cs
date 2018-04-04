using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Common;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm
{
    internal partial class ArmManager
    {
        public async Task<Site> LoadAsync(Site site)
        {
            await new[]
            {
                LoadSiteObjectAsync(site),
                LoadSitePublishingCredentialsAsync(site),
                LoadSiteConfigAsync(site)
            }
            //.IgnoreFailures()
            .WhenAll();
            return site;
        }

        public async Task<Site> LoadSiteObjectAsync(Site site)
        {
            var armSite = await ArmHttpAsync<ArmWrapper<ArmWebsite>>(HttpMethod.Get, ArmUriTemplates.Site.Bind(site));

            site.HostName = armSite.Properties.EnabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) == -1);
            site.ScmUri = armSite.Properties.EnabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) != -1);
            site.Location = armSite.Location;
            site.Kind = armSite.Kind;
            site.Sku = armSite.Properties.Sku;
            return site;
        }

        public async Task<Site> LoadSitePublishingCredentialsAsync(Site site)
        {
            return site
                .MergeWith(
                    await ArmHttpAsync<ArmWrapper<ArmWebsitePublishingCredentials>>(HttpMethod.Post, ArmUriTemplates.SitePublishingCredentials.Bind(site)),
                    t => t.Properties
                );
        }

        public async Task<Site> LoadSiteConfigAsync(Site site)
        {
            return site.MergeWith(
                    await ArmHttpAsync<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Get, ArmUriTemplates.SiteConfig.Bind(site)),
                    t => t.Properties
                );
        }

        public async Task<Site> UpdateSiteConfigAsync(Site site, object config)
        {
            return site.MergeWith(
                    await ArmHttpAsync<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Put, ArmUriTemplates.SiteConfig.Bind(site), config),
                    t => t.Properties
                );
        }

        public async Task<Dictionary<string, string>> GetFunctionAppAppSettings(Site site)
        {
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, string>>>(HttpMethod.Post, ArmUriTemplates.GetSiteAppSettings.Bind(site));
            return armResponse.Properties;
        }

        public async Task<HttpResult<Dictionary<string, string>, string>> UpdateFunctionAppAppSettings(Site site, IDictionary<string, string> appSettings)
        {
            var response = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.PutSiteAppSettings.Bind(site), new { properties = appSettings });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsAsync<ArmWrapper<Dictionary<string, string>>>();
                return new HttpResult<Dictionary<string, string>, string>(result.Properties);
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

        public async Task<Dictionary<string, AppServiceConnectionString>> GetFunctionAppConnectionStrings(Site functionApp)
        {
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, AppServiceConnectionString>>>(HttpMethod.Post, ArmUriTemplates.GetSiteConnectionStrings.Bind(functionApp));
            return armResponse.Properties;
        }
    }
}