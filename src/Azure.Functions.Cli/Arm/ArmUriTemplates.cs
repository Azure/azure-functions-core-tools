﻿using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Arm
{
    internal static class ArmUriTemplates
    {
        public const string ArmApiVersion = "2018-09-01";
        public const string WebsitesApiVersion = "2015-08-01";
        public const string SyncTriggersApiVersion = "2016-08-01";
        public const string ArmUrl = "https://management.azure.com";

        public static readonly ArmUriTemplate SubscriptionResourceByNameAndType = new ArmUriTemplate($"{ArmUrl}/resources?$filter=(SubscriptionId eq '{{subscriptionId}}' and name eq '{{resourceName}}' and resourceType eq '{{resourceType}}')", ArmApiVersion);
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