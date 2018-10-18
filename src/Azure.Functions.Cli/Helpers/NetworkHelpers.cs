using System;
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
            try
            {
                var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

                foreach (var tcpi in tcpConnInfoArray)
                {
                    // if state is TIME_WAIT that means the port was closed but not enough time has passed
                    // to ensure remote connections recieved termination ack. This can happen if the cli is used
                    // multiple times in a short period in a regular dev/test flow.
                    if (tcpi.LocalEndPoint.Port == port && tcpi.State != TcpState.TimeWait)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                // There are a number of reasons we can end up here...
                // The main one being running under WSL and this bug https://github.com/dotnet/corefx/issues/30909

                // Only realistic option is to assume port is free
                return true;
            }
        }
    }
}