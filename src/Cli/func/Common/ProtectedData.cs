// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Common
{
    public class ProtectedData
    {
        private static readonly ServiceProvider s_services;

        static ProtectedData()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDataProtection();
            s_services = serviceCollection.BuildServiceProvider();
        }

        public static byte[] Protect(byte[] data, string purpose)
        {
            var protector = s_services.GetDataProtector(purpose);
            return protector.Protect(data);
        }

        public static byte[] Unprotect(byte[] data, string purpose)
        {
            var protector = s_services.GetDataProtector(purpose);
            return protector.Unprotect(data);
        }
    }
}
