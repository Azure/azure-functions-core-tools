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
            // Check if the port is in the valid range
            // Port numbers are in the range of 0 to 65535, but ports below 1024 are reserved for system use
            if (startPort < 1024 || startPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(startPort), "Port number must be between 1024 and 65535.");
            }

            var usedPorts = new HashSet<int>();
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

            usedPorts.UnionWith(ipGlobalProperties
                                .GetActiveTcpConnections()
                                .Where(c => c.LocalEndPoint.Port >= startPort)
                                .Select(c => c.LocalEndPoint.Port));

            usedPorts.UnionWith(ipGlobalProperties
                                .GetActiveTcpListeners()
                                .Where(l => l.Port >= startPort)
                                .Select(l => l.Port));

            // Find the next available port starting from the specified port
            for (int port = startPort; port <= 65535; port++)
            {
                if (!usedPorts.Contains(port))
                {
                    return port;
                }
            }

            // If no port is available after the specified start port, return the first available port
            return GetAvailablePort();
        }
    }
}
