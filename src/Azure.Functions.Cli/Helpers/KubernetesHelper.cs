using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class KubernetesHelper
    {
        public static async Task RunKubectl(string cmd, bool showOutput = false)
        {
            var kubectl = new Executable("kubectl", cmd);

            if (!showOutput)
                await kubectl.RunAsync();
            else {
                await kubectl.RunAsync(l => ColoredConsole.WriteLine(l), e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            }
        }
    }
}