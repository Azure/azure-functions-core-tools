// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Extensions
{
    internal static class AssemblyExtensions
    {
        public static string GetInformationalVersion(this Assembly assembly)
        {
            var informationalVersion = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            return informationalVersion ?? string.Empty;
        }

        public static string GetCliVersion(this Assembly assembly)
        {
            // The package version is stamped into the assembly's AssemblyInformationalVersionAttribute at build time, followed by a '+'
            // and the commit hash, e.g.: "4.0.10000-dev+67bd99a8ce2ec3cf833f25c039f60222caf44573 (64-bit)"
            var version = assembly.GetInformationalVersion();

            if (version is not null)
            {
                var plusIndex = version.IndexOf('+');

                if (plusIndex > 0)
                {
                    return version[..plusIndex];
                }

                return version;
            }

            // Fallback to the file version, which is based on the CI build number, and then fallback to the assembly version, which is
            // product stable version, e.g. 4.0.0.0
            version = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version;

            return version;
        }
    }
}
