using System.Net;
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
    }
}