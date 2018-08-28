using System.Net.NetworkInformation;

namespace Azure.Functions.Cli.Helpers
{
    public static class NetworkHelpers
    {
        // https://stackoverflow.com/a/570461/3234163
        // There can be a race condition here between processes, but it's not very common.
        // If the race condition does occur, it'll fail later on in the binding step
        public static bool IsPortAvailable(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

            foreach (var tcpi in tcpConnInfoArray)
            {
                if (tcpi.LocalEndPoint.Port == port)
                {
                    return false;
                }
            }
            return true;
        }
    }
}