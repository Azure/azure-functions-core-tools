using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Azure.Functions.Cli.Helpers
{
    public static class NetworkHelpers
    {
        public static bool IsPortAvailable(int port)
        {
            try
            {
                var tcpListen = new TcpListener(IPAddress.Any, port);
                tcpListen.Start();
                tcpListen.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public static int GetNextAvailablePort(int startPort)
        {
            var usedPorts = new List<int>();
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

            usedPorts.AddRange(ipGlobalProperties
                                .GetActiveTcpConnections()
                                .Where(c => c.LocalEndPoint.Port >= startPort)
                                .Select(c => c.LocalEndPoint.Port));
            usedPorts.AddRange(ipGlobalProperties
                                .GetActiveTcpListeners()
                                .Where(l => l.Port >= startPort)
                                .Select(l => l.Port));

            usedPorts.Sort();

            foreach (var usedPort in usedPorts)
            {
                if (startPort < usedPort)
                {
                    return startPort;
                }
                startPort++;
            }

            return GetAvailablePort();
        }
    }
}