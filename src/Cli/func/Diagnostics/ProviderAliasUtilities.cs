// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Microsoft.Extensions.Logging
{
    internal static class ProviderAliasUtilities
    {
        internal static string GetAlias(Type providerType)
        {
            var attribute = providerType.GetCustomAttributes<ProviderAliasAttribute>(inherit: false).FirstOrDefault();
            return attribute?.Alias;
        }
    }
}
