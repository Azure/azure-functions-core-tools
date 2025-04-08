using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.NativeMethods
{
    internal static class EnvironmentNativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "SetEnvironmentVariable", SetLastError = true)]
        public static extern bool Win32SetEnvironmentVariable(string lpName, string lpValue);

        public static void SetEnvironmentVariable(string key, string value)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!Win32SetEnvironmentVariable(key, value))
                {
                    throw new OutOfMemoryException();
                }
            }
            else
            {
                // There is no safe native way to call into libc setenv on unix from .NET
                // .NET takes a snapshot of environ on process start and updates that snapshot.
                // Any changes through setenv() are ignored.
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine("Setting unsupported .NET environment variables (empty string) is not implemented for this platform.");
                }
            }
        }
    }
}
