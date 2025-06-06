﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Arm
{
    internal static class ArmUriTemplates
    {
        public const string ArmApiVersion = "2018-09-01";
        public const string WebsitesApiVersion = "2015-08-01";
        public const string FlexApiVersion = "2023-12-01";
        public const string SyncTriggersApiVersion = "2016-08-01";
        public const string ArgApiVersion = "2019-04-01";
        public const string FunctionAppOnContainerAppsApiVersion = "2022-09-01";
        public const string ManagedEnvironmentApiVersion = "2022-10-01";
        public const string BasicAuthCheckApiVersion = "2022-03-01";
        public const string FunctionsStacksApiVersion = "2020-10-01";
        public const string FlexFunctionsStacksApiVersion = "2020-10-01";

        public const string ArgUri = "providers/Microsoft.ResourceGraph/resources";

        public static readonly ArmUriTemplate SubscriptionResourceByNameAndType = new ArmUriTemplate($"resources?$filter=(SubscriptionId eq '{{subscriptionId}}' and name eq '{{resourceName}}' and resourceType eq '{{resourceType}}')", ArmApiVersion);
    }

    public class ArmUriTemplate
    {
        private readonly string _apiVersion;

        public ArmUriTemplate(string templateUrl, string apiVersion)
        {
            TemplateUrl = templateUrl;
            _apiVersion = "api-version=" + apiVersion;
        }

        public string TemplateUrl { get; private set; }

        public Uri Bind(string managementURL, object obj)
        {
            var completeTemplateUrl = $"{managementURL}/{TemplateUrl}";
            var dataBindings = Regex.Matches(completeTemplateUrl, "\\{(.*?)\\}").Cast<Match>().Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            var type = obj.GetType();
            var uriBuilder = new UriBuilder(dataBindings.Aggregate(completeTemplateUrl, (a, b) =>
            {
                var property = type.GetProperties().FirstOrDefault(p => p.Name.Equals(b, StringComparison.OrdinalIgnoreCase));
                if (property != null && property.CanRead)
                {
                    a = a.Replace(string.Format("{{{0}}}", b), property.GetValue(obj).ToString());
                }

                return a;
            }));
            var query = uriBuilder.Query.Trim('?');
            uriBuilder.Query = string.IsNullOrWhiteSpace(query) ? _apiVersion : string.Format("{0}&{1}", _apiVersion, query);
            return uriBuilder.Uri;
        }
    }
}
