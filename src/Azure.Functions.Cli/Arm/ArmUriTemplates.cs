using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Arm
{
    internal static class ArmUriTemplates
    {
        private const string armApiVersion = "2014-04-01";
        private const string websitesApiVersion = "2015-08-01";
        private const string storageApiVersion = "2015-05-01-preview";
        private const string ArmUrl = "https://management.azure.com";

        public static readonly ArmUriTemplate Subscriptions = new ArmUriTemplate($"{ArmUrl}/subscriptions", armApiVersion);
        public static readonly ArmUriTemplate Subscription = new ArmUriTemplate($"{Subscriptions.TemplateUrl}/{{subscriptionId}}", armApiVersion);
        public static readonly ArmUriTemplate SubscriptionWebApps = new ArmUriTemplate(Subscription.TemplateUrl + "/resources?$filter=resourceType eq 'Microsoft.Web/sites'", armApiVersion);

        public static readonly ArmUriTemplate ResourceGroups = new ArmUriTemplate($"{Subscription.TemplateUrl}/resourceGroups", armApiVersion);
        public static readonly ArmUriTemplate ResourceGroup = new ArmUriTemplate($"{ResourceGroups.TemplateUrl}/{{resourceGroupName}}", armApiVersion);
        public static readonly ArmUriTemplate ResourceGroupResources = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/resources", armApiVersion);

        public static readonly ArmUriTemplate WebsitesRegister = new ArmUriTemplate($"{Subscription.TemplateUrl}/providers/Microsoft.Web/register", websitesApiVersion);

        public static readonly ArmUriTemplate Sites = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/providers/Microsoft.Web/sites", websitesApiVersion);
        public static readonly ArmUriTemplate Site = new ArmUriTemplate($"{Sites.TemplateUrl}/{{siteName}}", websitesApiVersion);
        public static readonly ArmUriTemplate SitePublishingCredentials = new ArmUriTemplate($"{Site.TemplateUrl}/config/PublishingCredentials/list", websitesApiVersion);
        public static readonly ArmUriTemplate SiteConfig = new ArmUriTemplate($"{Site.TemplateUrl}/config/web", websitesApiVersion);
        public static readonly ArmUriTemplate GetSiteAppSettings = new ArmUriTemplate(Site.TemplateUrl + "/config/AppSettings/list", websitesApiVersion);
        public static readonly ArmUriTemplate PutSiteAppSettings = new ArmUriTemplate(Site.TemplateUrl + "/config/AppSettings", websitesApiVersion);
        public static readonly ArmUriTemplate GetSiteConnectionStrings = new ArmUriTemplate(Site.TemplateUrl + "/config/ConnectionStrings/list", websitesApiVersion);

        public static readonly ArmUriTemplate StorageRegister = new ArmUriTemplate($"{Subscription.TemplateUrl}/providers/Microsoft.Storage/register", storageApiVersion);
        public static readonly ArmUriTemplate StorageAccounts = new ArmUriTemplate($"{ResourceGroup.TemplateUrl}/providers/Microsoft.Storage/storageAccounts", storageApiVersion);
        public static readonly ArmUriTemplate StorageAccount = new ArmUriTemplate($"{StorageAccounts.TemplateUrl}/{{storageAccountName}}", storageApiVersion);
        public static readonly ArmUriTemplate StorageListKeys = new ArmUriTemplate($"{StorageAccount.TemplateUrl}/listKeys", storageApiVersion);
        public static readonly ArmUriTemplate SubscriptionStorageAccounts = new ArmUriTemplate(Subscription.TemplateUrl + "/resources?$filter=resourceType eq 'Microsoft.Storage/storageAccounts'", armApiVersion);

        public static readonly ArmUriTemplate PublishingUsers = new ArmUriTemplate($"{ArmUrl}/providers/Microsoft.Web/publishingUsers/web", websitesApiVersion);

        public static readonly ArmUriTemplate SubscriptionResourceByName = new ArmUriTemplate(Subscription.TemplateUrl + "/resources?$filter=name eq '{resourceName}'", armApiVersion);
    }

    public class ArmUriTemplate
    {
        public string TemplateUrl { get; private set; }
        private readonly string apiVersion;
        public ArmUriTemplate(string templateUrl, string apiVersion)
        {
            this.TemplateUrl = templateUrl;
            this.apiVersion = "api-version=" + apiVersion;
        }

        public Uri Bind(object obj)
        {
            var dataBindings = Regex.Matches(this.TemplateUrl, "\\{(.*?)\\}").Cast<Match>().Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            var type = obj.GetType();
            var uriBuilder = new UriBuilder(dataBindings.Aggregate(this.TemplateUrl, (a, b) =>
            {
                var property = type.GetProperties().FirstOrDefault(p => p.Name.Equals(b, StringComparison.OrdinalIgnoreCase));
                if (property != null && property.CanRead)
                {
                    a = a.Replace(string.Format("{{{0}}}", b), property.GetValue(obj).ToString());
                }
                return a;
            }));
            var query = uriBuilder.Query.Trim('?');
            uriBuilder.Query = string.IsNullOrWhiteSpace(query) ? this.apiVersion : string.Format("{0}&{1}", this.apiVersion, query);
            return uriBuilder.Uri;
        }
    }
}