using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Extensions;

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

        public async Task UpdateFunctionAppAppSettings(Site site, IDictionary<string, string> appSettings)
        {
            await ArmHttpAsync(HttpMethod.Put, ArmUriTemplates.PutSiteAppSettings.Bind(site), new { properties = appSettings });
        }

        public async Task<Dictionary<string, AppServiceConnectionString>> GetFunctionAppConnectionStrings(Site functionApp)
        {
            var armResponse = await ArmHttpAsync<ArmWrapper<Dictionary<string, AppServiceConnectionString>>>(HttpMethod.Post, ArmUriTemplates.GetSiteConnectionStrings.Bind(functionApp));
            return armResponse.Properties;
        }
    }
}