// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class TargetFrameworkHelper
    {
        // Regex sub-patterns for .NET Target Framework Monikers (TFMs)
        // Modern .NET (e.g., net6.0, net7.0-windows)
        private const string ModernNetPattern = @"net\d+\.\d+(?:-[a-z][a-z0-9]*(?:\d+(?:\.\d+)*)?)?";

        // netstandard and netcoreapp (e.g., netstandard2.0, netcoreapp3.1)
        private const string NetStandardCoreAppPattern = @"net(?:standard|coreapp)\d+(?:\.\d+)?";

        // Classic .NET Framework versions (e.g., net45, net48)
        private const string ClassicNetFrameworkPattern = @"net(?:10|11|20|35|40|403|45|451|452|46|461|462|47|471|472|48|481)";

        // Universal Windows Platform (UAP) (e.g., uap10.0)
        private const string UapPattern = @"uap\d+(?:\.\d+)*";

        // Windows Phone and Windows Phone App (e.g., wp8, wpa81)
        private const string WindowsPhonePattern = @"(?:wp(?:7|75|8|81)|wpa81)";

        // Silverlight (e.g., sl4, sl5)
        private const string SilverlightPattern = @"sl(?:4|5)";

        // Tizen (e.g., tizen4.0)
        private const string TizenPattern = @"tizen\d+(?:\.\d+)?";

        // NetNano (e.g., netnano1.0)
        private const string NetNanoPattern = @"netnano\d+(?:\.\d+)?";

        // .NET Micro Framework
        private const string NetMfPattern = @"netmf";

        // Legacy WinStore aliases (e.g., win8, win81, win10, netcore45, netcore50)
        private const string LegacyWinStorePattern = @"(?:win(?:8|81|10)|netcore(?:45|451|50)|netcore)";

        /// <summary>
        /// Regex that matches all valid .NET Target Framework Monikers (TFMs).
        /// Covers modern TFMs (netX.Y[-osversion]),
        /// netstandard, netcoreapp, classic .NET Framework, UAP, WP, Silverlight,
        /// Tizen, NetNano, NetMF, and legacy WinStore aliases.
        /// </summary>
        public static readonly Regex TfmRegex = new Regex(
            string.Join("|", new[]
            {
                ModernNetPattern,
                NetStandardCoreAppPattern,
                ClassicNetFrameworkPattern,
                UapPattern,
                WindowsPhonePattern,
                SilverlightPattern,
                TizenPattern,
                NetNanoPattern,
                NetMfPattern,
                LegacyWinStorePattern
            }),
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly IEnumerable<string> _supportedTargetFrameworks = [TargetFramework.Net10, TargetFramework.Net9, TargetFramework.Net8, TargetFramework.Net7, TargetFramework.Net6, TargetFramework.Net48];
        private static readonly IEnumerable<string> _supportedInProcTargetFrameworks = [TargetFramework.Net8, TargetFramework.Net6];

        public static IEnumerable<string> GetSupportedTargetFrameworks()
        {
            return _supportedTargetFrameworks;
        }

        public static IEnumerable<string> GetSupportedInProcTargetFrameworks()
        {
            return _supportedInProcTargetFrameworks;
        }
    }
}
