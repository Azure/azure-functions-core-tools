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
        public static async Task RunKubectl(string cmd)
        {
            var kubectl = new Executable("kubectl", cmd);
            await kubectl.RunAsync();
        }
    }
}