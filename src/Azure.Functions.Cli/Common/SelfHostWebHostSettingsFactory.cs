using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(string scriptPath)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets")
            };
        }
    }
}
