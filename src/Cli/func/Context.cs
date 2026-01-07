// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;

namespace Azure.Functions.Cli
{
    internal enum Context
    {
        None,

        [Description("Commands to log in to Azure and manage resources")]
        Azure,

        [Description("Commands to list and select Azure subscriptions")]
        Account,

        [Description("Commands for working with a local functions project")]
        FunctionApp,

        [Description("Commands to work with Azure Storage")]
        Storage,

        [Description("Commands for running the Functions host locally")]
        Host,

        [Description("Commands for creating and running functions locally")]
        Function,

        [Description("Commands to list and select active Azure subscription")]
        Subscriptions,

        [Description("Commands for managing environment settings for the local Functions host")]
        Settings,

        [Description("Commands for listing available function templates")]
        Templates,

        [Description("Commands for installing extensions")]
        Extensions,

        [Description("Commands for managing extension bundles")]
        Bundles,

        [Description("Commands for working with Durable Functions")]
        Durable,

        [Description("Commands for working with Kubernetes and Azure Functions")]
        Kubernetes,

        [Description("Commands for working with Container Service and Azure Functions")]
        AzureContainerApps,
    }

#pragma warning disable SA1649 // File name should match first type name
    internal static class ContextEnumExtensions
#pragma warning restore SA1649 // File name should match first type name
    {
        public static string ToLowerCaseString(this Context context)
        {
            return context.ToString().ToLowerInvariant();
        }
    }
}
