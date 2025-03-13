// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Net.NetworkInformation;
using System.ComponentModel;
using Azure.Functions.Cli.Common;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Telemetry
{
    internal static class MACAddressGetter
    {
        private const string MACRegex = @"(?:[a-z0-9]{2}[:\-]){5}[a-z0-9]{2}";
        private const string ZeroRegex = @"(?:00[:\-]){5}00";
        private const int ErrorFileNotFound = 0x2;

        public static string GetMACAddress()
        {
            try
            {
                var shelloutput = GetShellOutMACAddressOutput().GetAwaiter().GetResult();
                if (shelloutput == null)
                {
                    return null;
                }

                return ParseMACAddress(shelloutput);
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode == ErrorFileNotFound)
                {
                    return GetMACAddressByNetworkInterface();
                }
                else
                {
                    throw;
                }
            }
        }

        private static string ParseMACAddress(string shelloutput)
        {
            string macAddress = null;
            foreach (Match match in Regex.Matches(shelloutput, MACRegex, RegexOptions.IgnoreCase))
            {
                if (!Regex.IsMatch(match.Value, ZeroRegex))
                {
                    macAddress = match.Value;
                    break;
                }
            }

            if (macAddress != null)
            {
                return macAddress;
            }
            return null;
        }

        private static async Task<string> GetIpCommandOutput()
        {
            return await ExecuteAndOutput("ip", "link");
        }

        private static async Task<string> GetShellOutMACAddressOutput()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await ExecuteAndOutput("getmac.exe", null);
            }
            else
            {
                try
                {
                    var ifConfigResult = await ExecuteAndOutput("ifconfig", "-a");

                    if (!string.IsNullOrEmpty(ifConfigResult))
                    {
                        return ifConfigResult;
                    }
                    else
                    {
                        return await GetIpCommandOutput();
                    }
                }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode == ErrorFileNotFound)
                    {
                        return await GetIpCommandOutput();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private static async Task<string> ExecuteAndOutput(string command, string args)
        {
            var exe = new Executable(command, args);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitCode = await exe.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
            if (exitCode == 0)
            {
                return stdout.ToString();
            }

            return null;
        }

        private static string GetMACAddressByNetworkInterface()
        {
            return GetMACAddressesByNetworkInterface().FirstOrDefault();
        }

        private static List<string> GetMACAddressesByNetworkInterface()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            var macs = new List<string>();

            if (nics == null || nics.Length < 1)
            {
                macs.Add(string.Empty);
                return macs;
            }

            foreach (NetworkInterface adapter in nics)
            {
                IPInterfaceProperties properties = adapter.GetIPProperties();

                PhysicalAddress address = adapter.GetPhysicalAddress();
                byte[] bytes = address.GetAddressBytes();
                macs.Add(string.Join("-", bytes.Select(x => x.ToString("X2"))));
                if (macs.Count >= 10)
                {
                    break;
                }
            }
            return macs;
        }
    }
}
