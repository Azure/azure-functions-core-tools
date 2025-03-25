// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
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
