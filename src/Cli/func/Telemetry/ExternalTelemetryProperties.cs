// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Win32;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

namespace Azure.Functions.Cli.Telemetry
{
    // Some properties we need for telemetry, that don't yet have suitable
    // public API
    internal static class ExternalTelemetryProperties
    {
        /// <summary>
        /// For Windows, returns the OS installation type, eg. "Nano Server", "Server Core", "Server", or "Client".
        /// For Unix, or on error, currently returns empty string.
        /// </summary>
        internal static string GetInstallationType()
        {
            const string Key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            const string ValueName = @"InstallationType";

            try
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Registry.GetValue(Key, ValueName, defaultValue: string.Empty) as string
                    : string.Empty;
            }

            // Catch everything: this is for telemetry only.
            catch (Exception e)
            {
                Debug.Assert(e is ArgumentException | e is SecurityException | e is InvalidCastException, string.Empty);
                return string.Empty;
            }
        }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = false)]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern bool GetProductInfo(uint dwOSMajorVersion, uint dwOSMinorVersion, uint dwSpMajorVersion, uint dwSpMinorVersion, out uint pdwReturnedProductType);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

        /// <summary>
        /// For Windows, returns the product type, loosely the SKU, as encoded by GetProductInfo().
        /// For example, Enterprise is "4" (0x4) and Professional is "48" (0x30)
        /// See https://msdn.microsoft.com/en-us/library/windows/desktop/ms724358(v=vs.85).aspx for the full list.
        /// We're not attempting to decode the value on the client side as new Windows releases may add new values.
        /// For Unix, or on error, returns an empty string.
        /// </summary>
        internal static string GetProductType()
        {
            if (RuntimeEnvironment.OperatingSystemPlatform != Platform.Windows)
            {
                return string.Empty;
            }

            try
            {
                if (GetProductInfo((uint)Environment.OSVersion.Version.Major, (uint)Environment.OSVersion.Version.Minor, 0, 0, out uint productType))
                {
                    return productType.ToString("D", CultureInfo.InvariantCulture);
                }
            }

            // Catch everything: this is for telemetry only
            catch (Exception e)
            {
                Debug.Assert(false, $"Unexpected exception from GetProductInfo: ${e.GetType().Name}: ${e.Message}");
            }

            return string.Empty;
        }

        [DllImport("libc", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern IntPtr gnu_get_libc_release();
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
#pragma warning restore SA1300 // Element should begin with upper-case letter

        [DllImport("libc", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern IntPtr gnu_get_libc_version();
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
#pragma warning restore SA1300 // Element should begin with upper-case letter

        /// <summary>
        /// If gnulibc is available, returns the release, such as "stable".
        /// If the libc is musl, currently returns empty string.
        /// Otherwise returns empty string.
        /// </summary>
        internal static string GetLibcRelease()
        {
            if (RuntimeEnvironment.OperatingSystemPlatform == Platform.Windows)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8(gnu_get_libc_release());
            }

            // Catch everything: this is for telemetry only
            catch (Exception e)
            {
                Debug.Assert(e is DllNotFoundException or EntryPointNotFoundException, string.Empty);
                return string.Empty;
            }
        }

        /// <summary>
        /// If gnulibc is available, returns the version, such as "2.22".
        /// If the libc is musl, currently returns empty string. (In future could run "ldd -version".)
        /// Otherwise returns empty string.
        /// </summary>
        internal static string GetLibcVersion()
        {
            if (RuntimeEnvironment.OperatingSystemPlatform == Platform.Windows)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8(gnu_get_libc_version());
            }

            // Catch everything: this is for telemetry only
            catch (Exception e)
            {
                Debug.Assert(e is DllNotFoundException || e is EntryPointNotFoundException, string.Empty);
                return string.Empty;
            }
        }
    }
}
